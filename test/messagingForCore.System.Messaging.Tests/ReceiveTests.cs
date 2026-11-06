// Licensed under the MIT License. Copyright (c) messagingForCore contributors.
//
// Issue #1: receive and peek with timeouts; a waiting receive wakes when a message is sent; BeginReceive/BeginPeek
// and the ReceiveCompleted/PeekCompleted events, on MSMQ's callback path.
using System;
using System.Diagnostics;
using System.Messaging;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace MessagingForCore.Tests
{
    [TestFixture]
    public class ReceiveTests
    {
        [Test]
        public void Timeout()
        {
            using var queue = TestDatabase.NewQueue();
            var watch = Stopwatch.StartNew();
            Assert.That(TestDatabase.ErrorOf(() => queue.Receive(TimeSpan.FromMilliseconds(500))), Is.EqualTo(MessageQueueErrorCode.IOTimeout));
            Assert.That(watch.ElapsedMilliseconds, Is.GreaterThanOrEqualTo(450));
            Assert.That(TestDatabase.ErrorOf(() => queue.Peek(TimeSpan.Zero)), Is.EqualTo(MessageQueueErrorCode.IOTimeout));
        }

        [Test]
        public void WaitingReceiveWakesOnSend()
        {
            using var queue = TestDatabase.NewQueue();
            using var sender = new MessageQueue(queue.Path) { Formatter = new BinaryMessageFormatter() };
            var send = Task.Run(() =>
            {
                Thread.Sleep(300);
                sender.Send("late");
            });
            var watch = Stopwatch.StartNew();
            Assert.That(queue.Receive(TimeSpan.FromSeconds(30)).Body, Is.EqualTo("late"));
            Assert.That(watch.ElapsedMilliseconds, Is.LessThan(10_000));
            send.Wait();
        }

        [Test]
        public void ConcurrentReceiversGetEachMessageOnce()
        {
            using var queue = TestDatabase.NewQueue();
            const int count = 40;
            for (int i = 0; i < count; i++)
                queue.Send(i);
            var received = new System.Collections.Concurrent.ConcurrentBag<int>();
            Parallel.For(0, 4, _ =>
            {
                using var receiver = new MessageQueue(queue.Path) { Formatter = new BinaryMessageFormatter() };
                while (true)
                {
                    try
                    {
                        received.Add((int)receiver.Receive(TimeSpan.Zero).Body);
                    }
                    catch (MessageQueueException e) when (e.MessageQueueErrorCode == MessageQueueErrorCode.IOTimeout)
                    {
                        return;
                    }
                }
            });
            Assert.That(received, Is.EquivalentTo(System.Linq.Enumerable.Range(0, count)));
        }

        [Test]
        public void BeginReceiveWaitsForAMessage()
        {
            using var queue = TestDatabase.NewQueue();
            IAsyncResult result = queue.BeginReceive(TimeSpan.FromSeconds(30));
            Assert.That(result.IsCompleted, Is.False);
            queue.Send("async");
            Assert.That(result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(30)), Is.True);
            Assert.That(queue.EndReceive(result).Body, Is.EqualTo("async"));
            Assert.That(TestDatabase.ErrorOf(() => queue.Peek(TimeSpan.Zero)), Is.EqualTo(MessageQueueErrorCode.IOTimeout));
        }

        [Test]
        public void BeginReceiveTimeout()
        {
            using var queue = TestDatabase.NewQueue();
            IAsyncResult result = queue.BeginReceive(TimeSpan.FromMilliseconds(200));
            Assert.That(result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(30)), Is.True);
            Assert.That(TestDatabase.ErrorOf(() => queue.EndReceive(result)), Is.EqualTo(MessageQueueErrorCode.IOTimeout));
        }

        [Test]
        public void ReceiveCompletedLoop()
        {
            // The usual service loop: each ReceiveCompleted handler ends the receive and begins the next.
            using var queue = TestDatabase.NewQueue();
            var bodies = new System.Collections.Concurrent.ConcurrentQueue<string>();
            using var done = new CountdownEvent(3);
            queue.ReceiveCompleted += (sender, e) =>
            {
                var q = (MessageQueue)sender;
                bodies.Enqueue((string)q.EndReceive(e.AsyncResult).Body);
                done.Signal();
                if (done.CurrentCount > 0)
                    q.BeginReceive();
            };
            queue.BeginReceive();
            foreach (string body in new[] { "1", "2", "3" })
                queue.Send(body);
            Assert.That(done.Wait(TimeSpan.FromSeconds(30)), Is.True);
            Assert.That(bodies, Is.EqualTo(new[] { "1", "2", "3" }));
        }

        [Test]
        public void BeginPeekAndPeekCompleted()
        {
            using var queue = TestDatabase.NewQueue();
            string peeked = null;
            using var done = new ManualResetEventSlim();
            queue.PeekCompleted += (sender, e) =>
            {
                peeked = (string)((MessageQueue)sender).EndPeek(e.AsyncResult).Body;
                done.Set();
            };
            queue.BeginPeek(TimeSpan.FromSeconds(30));
            queue.Send("peek me");
            Assert.That(done.Wait(TimeSpan.FromSeconds(30)), Is.True);
            Assert.That(peeked, Is.EqualTo("peek me"));
            Assert.That(queue.Receive(TimeSpan.Zero).Body, Is.EqualTo("peek me"), "BeginPeek leaves the message");
        }

        [Test]
        public void BeginReceiveWithLargeBody()
        {
            // The buffer-size retry on the asynchronous path (RaiseCompletionEvent).
            using var queue = TestDatabase.NewQueue();
            string body = new string('x', 50_000);
            queue.Send(body);
            IAsyncResult result = queue.BeginReceive(TimeSpan.FromSeconds(30));
            Assert.That(result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(30)), Is.True);
            Assert.That(queue.EndReceive(result).Body, Is.EqualTo(body));
        }

        [Test]
        public void CloseCancelsBeginReceive()
        {
            var queue = TestDatabase.NewQueue();
            IAsyncResult result = queue.BeginReceive(MessageQueue.InfiniteTimeout);
            queue.Close();
            Assert.That(result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(30)), Is.True);
            Assert.That(TestDatabase.ErrorOf(() => queue.EndReceive(result)), Is.EqualTo(MessageQueueErrorCode.OperationCanceled));
        }
    }
}
