// Licensed under the MIT License. Copyright (c) messagingForCore contributors.
//
// Issue #1, "Message functions" and "Message properties": send, receive and peek; the properties the store keeps; the
// read filter; MSMQ's order; and MSMQ's buffer-size protocol (the store says how big a buffer must be, Microsoft's
// code grows it and asks again).
using System;
using System.IO;
using System.Linq;
using System.Messaging;
using System.Text;
using NUnit.Framework;

namespace MessagingForCore.Tests
{
    [TestFixture]
    public class MessageTests
    {
        [Test]
        public void Properties()
        {
            using var queue = TestDatabase.NewQueue();
            using var responses = TestDatabase.NewQueue();
            queue.MessageReadPropertyFilter.SetAll();
            var sent = new Message("body", new BinaryMessageFormatter())
            {
                Label = "a label",
                CorrelationId = Guid.NewGuid() + @"\12",
                Priority = MessagePriority.High,
                AppSpecific = 42,
                Extension = new byte[] { 1, 2, 3 },
                ResponseQueue = responses,
                TimeToBeReceived = TimeSpan.FromHours(1),
                UseJournalQueue = true,
                Recoverable = true,
            };
            queue.Send(sent);
            StringAssert.StartsWith(MessageQueue.GetMachineId(".").ToString() + @"\", sent.Id, "Send gives the new message's ID");

            Message received = queue.Receive(TimeSpan.Zero);
            Assert.That(received.Id, Is.EqualTo(sent.Id));
            Assert.That(received.Body, Is.EqualTo("body"));
            Assert.That(received.Label, Is.EqualTo("a label"));
            Assert.That(received.CorrelationId, Is.EqualTo(sent.CorrelationId));
            Assert.That(received.Priority, Is.EqualTo(MessagePriority.High));
            Assert.That(received.AppSpecific, Is.EqualTo(42));
            Assert.That(received.Extension, Is.EqualTo(new byte[] { 1, 2, 3 }));
            Assert.That(received.ResponseQueue.FormatName, Is.EqualTo(responses.FormatName));
            Assert.That(received.DestinationQueue.FormatName, Is.EqualTo(queue.FormatName));
            Assert.That(received.BodyType, Is.EqualTo(0x300), "BinaryMessageFormatter's VT_BINARY_OBJECT");
            Assert.That(received.SentTime, Is.EqualTo(DateTime.Now).Within(TimeSpan.FromMinutes(1)));
            Assert.That(received.ArrivedTime, Is.EqualTo(DateTime.Now).Within(TimeSpan.FromMinutes(1)));
            Assert.That(received.SourceMachine, Is.EqualTo(Environment.MachineName).IgnoreCase);
            Assert.That(received.LookupId, Is.GreaterThan(0));
            Assert.That(received.MessageType, Is.EqualTo(MessageType.Normal));
            // Kept as sent, but not acted on (issue #1).
            Assert.That(received.TimeToBeReceived, Is.EqualTo(TimeSpan.FromHours(1)));
            Assert.That(received.UseJournalQueue, Is.True);
            Assert.That(received.Recoverable, Is.True);
            Assert.That(received.TimeToReachQueue, Is.EqualTo(Message.InfiniteTimeout));
        }

        [Test]
        public void Defaults()
        {
            using var queue = TestDatabase.NewQueue();
            queue.MessageReadPropertyFilter.SetAll();
            queue.Send(new Message("x", new BinaryMessageFormatter()));
            Message received = queue.Receive(TimeSpan.Zero);
            Assert.That(received.Label, Is.EqualTo(""));
            Assert.That(received.Priority, Is.EqualTo(MessagePriority.Normal));
            Assert.That(received.CorrelationId, Is.EqualTo(Guid.Empty + @"\0"));
            Assert.That(received.ResponseQueue, Is.Null);
            Assert.That(received.Extension, Is.Empty);
            Assert.That(received.TimeToBeReceived, Is.EqualTo(Message.InfiniteTimeout));
            Assert.That(received.IsFirstInTransaction, Is.False);
        }

        [Test]
        public void FilterChoosesTheProperties()
        {
            using var queue = TestDatabase.NewQueue();
            queue.Send(new Message("x", new BinaryMessageFormatter()) { Label = "label", AppSpecific = 7 });
            queue.MessageReadPropertyFilter.ClearAll();
            queue.MessageReadPropertyFilter.Body = true;
            Message received = queue.Receive(TimeSpan.Zero);
            Assert.That(received.Body, Is.EqualTo("x"));
            Assert.Throws<InvalidOperationException>(() => _ = received.Label);
            Assert.Throws<InvalidOperationException>(() => _ = received.AppSpecific);
        }

        [Test]
        public void PriorityThenArrival()
        {
            using var queue = TestDatabase.NewQueue();
            var order = new[]
            {
                ("low", MessagePriority.Low), ("normal 1", MessagePriority.Normal), ("highest", MessagePriority.Highest),
                ("normal 2", MessagePriority.Normal), ("lowest", MessagePriority.Lowest), ("normal 3", MessagePriority.Normal),
            };
            foreach (var (body, priority) in order)
                queue.Send(new Message(body, new BinaryMessageFormatter()) { Priority = priority });
            string[] received = Enumerable.Range(0, order.Length).Select(_ => (string)queue.Receive(TimeSpan.Zero).Body).ToArray();
            Assert.That(received, Is.EqualTo(new[] { "highest", "normal 1", "normal 2", "normal 3", "low", "lowest" }));
        }

        [Test]
        public void PeekLeavesTheMessage()
        {
            using var queue = TestDatabase.NewQueue();
            queue.Send("x");
            Assert.That(queue.Peek(TimeSpan.Zero).Body, Is.EqualTo("x"));
            Assert.That(queue.Peek(TimeSpan.Zero).Body, Is.EqualTo("x"));
            Assert.That(queue.Receive(TimeSpan.Zero).Body, Is.EqualTo("x"));
            Assert.That(TestDatabase.ErrorOf(() => queue.Peek(TimeSpan.Zero)), Is.EqualTo(MessageQueueErrorCode.IOTimeout));
        }

        [Test]
        public void BodyStream()
        {
            using var queue = TestDatabase.NewQueue();
            byte[] bytes = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
            queue.Send(new Message { BodyStream = new MemoryStream(bytes), BodyType = 17 });
            Message received = queue.Receive(TimeSpan.Zero);
            Assert.That(received.BodyType, Is.EqualTo(17));
            var copy = new MemoryStream();
            received.BodyStream.CopyTo(copy);
            Assert.That(copy.ToArray(), Is.EqualTo(bytes));
        }

        // The buffer-size protocol: buffers smaller than the values.

        [Test]
        public void LargerThanTheBuffers()
        {
            using var queue = TestDatabase.NewQueue();
            queue.MessageReadPropertyFilter.SetAll();
            queue.MessageReadPropertyFilter.DefaultBodySize = 8;
            queue.MessageReadPropertyFilter.DefaultLabelSize = 4;
            queue.MessageReadPropertyFilter.DefaultExtensionSize = 2;
            string body = new string('b', 100_000);
            string label = new string('l', 200);
            byte[] extension = Enumerable.Range(0, 1000).Select(i => (byte)i).ToArray();
            queue.Send(new Message(body, new BinaryMessageFormatter()) { Label = label, Extension = extension });

            Message peeked = queue.Peek(TimeSpan.Zero);
            Assert.That(peeked.Body, Is.EqualTo(body));
            Message received = queue.Receive(TimeSpan.Zero);
            Assert.That(received.Body, Is.EqualTo(body));
            Assert.That(received.Label, Is.EqualTo(label));
            Assert.That(received.Extension, Is.EqualTo(extension));
            Assert.That(TestDatabase.ErrorOf(() => queue.Receive(TimeSpan.Zero)), Is.EqualTo(MessageQueueErrorCode.IOTimeout),
                        "The retry received the message once");
        }

        [Test]
        public void MessageOverFourMegabytes()
        {
            using var queue = TestDatabase.NewQueue();
            var message = new Message { BodyStream = new MemoryStream(new byte[4 * 1024 * 1024 + 1]) };
            Assert.That(TestDatabase.ErrorOf(() => queue.Send(message)), Is.EqualTo(MessageQueueErrorCode.InsufficientResources));
            queue.Send(new Message { BodyStream = new MemoryStream(new byte[4 * 1024 * 1024]) });
        }

        [Test]
        public void UnicodeLabel()
        {
            using var queue = TestDatabase.NewQueue();
            string label = "Grüße, 東京, \U0001F600";
            queue.Send("x", label);
            Assert.That(queue.Receive(TimeSpan.Zero).Label, Is.EqualTo(label));
        }

        // By lookup ID (MSMQ 3.0).

        [Test]
        public void ByLookupId()
        {
            using var queue = TestDatabase.NewQueue();
            foreach (string body in new[] { "1", "2", "3" })
                queue.Send(body);
            long[] ids = queue.GetAllMessages().Select(m => m.LookupId).ToArray();
            Assert.That(ids, Is.Ordered.Ascending);

            Assert.That(queue.PeekByLookupId(ids[1]).Body, Is.EqualTo("2"));
            Assert.That(queue.PeekByLookupId(MessageLookupAction.Next, ids[1]).Body, Is.EqualTo("3"));
            Assert.That(queue.PeekByLookupId(MessageLookupAction.Previous, ids[1]).Body, Is.EqualTo("1"));
            Assert.That(queue.PeekByLookupId(MessageLookupAction.First, 0).Body, Is.EqualTo("1"));
            Assert.That(queue.PeekByLookupId(MessageLookupAction.Last, 0).Body, Is.EqualTo("3"));

            Assert.That(queue.ReceiveByLookupId(ids[1]).Body, Is.EqualTo("2"));
            Assert.Throws<InvalidOperationException>(() => queue.PeekByLookupId(ids[1]), "MessageNotFound");
            Assert.That(queue.PeekByLookupId(MessageLookupAction.Next, ids[1]).Body, Is.EqualTo("3"));
            Assert.That(queue.ReceiveByLookupId(MessageLookupAction.Last, 0, MessageQueueTransactionType.None).Body, Is.EqualTo("3"));
        }

        [Test]
        public void ByIdAndCorrelationId()
        {
            using var queue = TestDatabase.NewQueue();
            var first = new Message("first", new BinaryMessageFormatter());
            var second = new Message("second", new BinaryMessageFormatter());
            queue.Send(first);
            second.CorrelationId = first.Id;
            queue.Send(second);
            Assert.That(queue.PeekById(second.Id).Body, Is.EqualTo("second"));
            Assert.That(queue.ReceiveByCorrelationId(first.Id).Body, Is.EqualTo("second"));
            Assert.That(queue.ReceiveById(first.Id).Body, Is.EqualTo("first"));
            Assert.Throws<InvalidOperationException>(() => queue.ReceiveById(first.Id));
        }
    }
}
