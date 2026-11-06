// Licensed under the MIT License. Copyright (c) messagingForCore contributors.
//
// Issue #1: cursors (PeekNext, GetMessageEnumerator2). An enumerator peeks the first message with PEEK_CURRENT and
// the others with PEEK_NEXT; when a buffer is too small, Microsoft's code retries a PEEK_NEXT with PEEK_CURRENT, so
// the store moves the cursor even then and nothing is skipped.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Messaging;
using NUnit.Framework;

namespace MessagingForCore.Tests
{
    [TestFixture]
    public class CursorTests
    {
        private static List<string> Enumerate(MessageQueue queue)
        {
            var bodies = new List<string>();
            using (MessageEnumerator messages = queue.GetMessageEnumerator2())
            {
                while (messages.MoveNext())
                    bodies.Add((string)messages.Current.Body);
            }
            return bodies;
        }

        [Test]
        public void EnumeratorInQueueOrder()
        {
            using var queue = TestDatabase.NewQueue();
            queue.Send(new Message("normal", new BinaryMessageFormatter()));
            queue.Send(new Message("high", new BinaryMessageFormatter()) { Priority = MessagePriority.High });
            queue.Send(new Message("normal 2", new BinaryMessageFormatter()));
            Assert.That(Enumerate(queue), Is.EqualTo(new[] { "high", "normal", "normal 2" }));
            Assert.That(queue.GetAllMessages().Select(m => m.Body), Is.EqualTo(new[] { "high", "normal", "normal 2" }));
            Assert.That(Enumerate(queue).Count, Is.EqualTo(3), "Peeking leaves the messages");
        }

        [Test]
        public void EnumeratorWithSmallBuffers()
        {
            using var queue = TestDatabase.NewQueue();
            queue.MessageReadPropertyFilter.DefaultBodySize = 8;
            string[] bodies = Enumerable.Range(0, 5).Select(i => i + new string('x', 500)).ToArray();
            foreach (string body in bodies)
                queue.Send(body);
            Assert.That(Enumerate(queue), Is.EqualTo(bodies));
        }

        [Test]
        public void EmptyQueue()
        {
            using var queue = TestDatabase.NewQueue();
            Assert.That(Enumerate(queue), Is.Empty);
        }

        [Test]
        public void RemoveCurrent()
        {
            using var queue = TestDatabase.NewQueue();
            foreach (string body in new[] { "keep 1", "remove", "keep 2" })
                queue.Send(body);
            using (MessageEnumerator messages = queue.GetMessageEnumerator2())
            {
                while (messages.MoveNext())
                {
                    if ((string)messages.Current.Body == "remove")
                    {
                        Assert.That(messages.RemoveCurrent().Body, Is.EqualTo("remove"));
                        break;
                    }
                }
            }
            Assert.That(Enumerate(queue), Is.EqualTo(new[] { "keep 1", "keep 2" }));
        }

        [Test]
        public void RemoveEveryMessageWithReset()
        {
            // The usual way to drain a queue with an enumerator: RemoveCurrent, then Reset.
            using var queue = TestDatabase.NewQueue();
            for (int i = 0; i < 4; i++)
                queue.Send(i.ToString());
            var removed = new List<string>();
            using (MessageEnumerator messages = queue.GetMessageEnumerator2())
            {
                while (messages.MoveNext())
                {
                    removed.Add((string)messages.RemoveCurrent().Body);
                    messages.Reset();
                }
            }
            Assert.That(removed, Is.EqualTo(new[] { "0", "1", "2", "3" }));
            Assert.That(Enumerate(queue), Is.Empty);
        }

        [Test]
        public void CursorPeekNextAndReceive()
        {
            using var queue = TestDatabase.NewQueue();
            foreach (string body in new[] { "1", "2", "3" })
                queue.Send(body);
            using Cursor cursor = queue.CreateCursor();
            Assert.That(queue.Peek(TimeSpan.Zero, cursor, PeekAction.Current).Body, Is.EqualTo("1"));
            Assert.That(queue.Peek(TimeSpan.Zero, cursor, PeekAction.Next).Body, Is.EqualTo("2"));
            Assert.That(queue.Peek(TimeSpan.Zero, cursor, PeekAction.Current).Body, Is.EqualTo("2"));
            Assert.That(queue.Receive(TimeSpan.Zero, cursor).Body, Is.EqualTo("2"));
            // The receive moved the cursor to the next message.
            Assert.That(queue.Peek(TimeSpan.Zero, cursor, PeekAction.Current).Body, Is.EqualTo("3"));
            Assert.That(TestDatabase.ErrorOf(() => queue.Peek(TimeSpan.Zero, cursor, PeekAction.Next)), Is.EqualTo(MessageQueueErrorCode.IOTimeout));
            Assert.That(queue.GetAllMessages().Select(m => m.Body), Is.EqualTo(new[] { "1", "3" }));
        }

        [Test]
        public void PeekNextWaitsForTheNextMessage()
        {
            using var queue = TestDatabase.NewQueue();
            queue.Send("1");
            using Cursor cursor = queue.CreateCursor();
            Assert.That(queue.Peek(TimeSpan.Zero, cursor, PeekAction.Current).Body, Is.EqualTo("1"));
            var later = new System.Threading.Timer(_ => queue.Send("2"), null, 300, System.Threading.Timeout.Infinite);
            Assert.That(queue.Peek(TimeSpan.FromSeconds(10), cursor, PeekAction.Next).Body, Is.EqualTo("2"));
            later.Dispose();
        }
    }
}
