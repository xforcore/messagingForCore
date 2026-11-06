// Licensed under the MIT License. Copyright (c) messagingForCore contributors.
//
// Issue #1, "Gate test": two processes on one database file. This process and messagingForCore.Gate.Peer send,
// receive, peek and transact on the same queues: order and IDs agree across processes, a waiting receive sees another
// process's message (by polling), a transaction across two queues commits or aborts in another process, and one left
// open by a process that ended is aborted by the next process on the host.
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Messaging;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace MessagingForCore.Gate
{
    [TestFixture]
    [NonParallelizable]
    public class GateTests
    {
        private string folder;
        private string database;

        [OneTimeSetUp]
        public void Install()
        {
            folder = Path.Combine(TestContext.CurrentContext.WorkDirectory, "gate-" + Guid.NewGuid().ToString("N"));
            database = Path.Combine(folder, "queues.db");
            MessagingForCore.Msmq.Schema.Install(database);
            AppContext.SetData("MessagingForCore.Database", database);
        }

        [OneTimeTearDown]
        public void Remove()
        {
            AppContext.SetData("MessagingForCore.Database", null);
            SqliteConnection.ClearAllPools();
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
            }
        }

        private static string NewQueue(bool transactional = false)
        {
            string path = @".\private$\gate-" + Guid.NewGuid().ToString("N");
            MessageQueue.Create(path, transactional).Dispose();
            return path;
        }

        private static MessageQueue Open(string path)
        {
            var queue = new MessageQueue(path) { Formatter = new BinaryMessageFormatter() };
            queue.MessageReadPropertyFilter.SetAll();
            return queue;
        }

        /// <summary>Starts the peer on this test's database; the caller waits for it.</summary>
        private Process StartPeer(params string[] args)
        {
            var start = new ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "messagingForCore.Gate.Peer.dll"));
            foreach (string arg in args)
                start.ArgumentList.Add(arg);
            start.Environment["MESSAGINGFORCORE_DB"] = database;
            return Process.Start(start);
        }

        private static string[] Wait(Process peer)
        {
            using (peer)
            {
                string output = peer.StandardOutput.ReadToEnd();
                string error = peer.StandardError.ReadToEnd();
                if (!peer.WaitForExit(60_000))
                {
                    peer.Kill(entireProcessTree: true);
                    Assert.Fail("The peer didn't finish in 60 seconds");
                }
                Assert.That(peer.ExitCode, Is.EqualTo(0), error);
                return output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToArray();
            }
        }

        private string[] Peer(params string[] args) => Wait(StartPeer(args));

        [Test]
        public void SendHereReceiveThere()
        {
            string path = NewQueue();
            using (MessageQueue queue = Open(path))
            {
                queue.Send(new Message("low", new BinaryMessageFormatter()) { Priority = MessagePriority.Low });
                queue.Send(new Message("high", new BinaryMessageFormatter()) { Priority = MessagePriority.High });
            }
            Assert.That(Peer("bodies", path), Is.EqualTo(new[] { "high", "low" }));
            Assert.That(Peer("receive", path, "0"), Is.EqualTo(new[] { "high" }));
            Assert.That(Peer("receive", path, "0"), Is.EqualTo(new[] { "low" }));
        }

        [Test]
        public void SendThereReceiveHere()
        {
            string path = NewQueue();
            string id = Peer("send", path, "first", "Normal", "-")[0];
            string correlated = Peer("send", path, "second", "Normal", id)[0];
            Peer("send", path, "urgent", "Highest", "-");

            using MessageQueue queue = Open(path);
            Assert.That(queue.GetAllMessages().Select(m => m.Body), Is.EqualTo(new[] { "urgent", "first", "second" }));
            Message byId = queue.PeekById(id);
            Assert.That(byId.Body, Is.EqualTo("first"));
            Assert.That(queue.PeekByCorrelationId(id).Id, Is.EqualTo(correlated));
            Assert.That(queue.PeekByLookupId(MessageLookupAction.Next, byId.LookupId).Body, Is.EqualTo("second"));
            using (Cursor cursor = queue.CreateCursor())
            {
                Assert.That(queue.Peek(TimeSpan.Zero, cursor, PeekAction.Current).Body, Is.EqualTo("urgent"));
                Assert.That(queue.Peek(TimeSpan.Zero, cursor, PeekAction.Next).Body, Is.EqualTo("first"));
            }
            Assert.That(queue.ReceiveByCorrelationId(id).Body, Is.EqualTo("second"));
            Assert.That(queue.ReceiveById(id).Body, Is.EqualTo("first"));
            Assert.That(queue.ReceiveByLookupId(MessageLookupAction.First, 0, MessageQueueTransactionType.None).Body, Is.EqualTo("urgent"));
        }

        [Test]
        public void WaitingReceiveSeesAnotherProcess()
        {
            string path = NewQueue();
            Process peer = StartPeer("send", path, "from the peer", "Normal", "-", "1000");
            var watch = Stopwatch.StartNew();
            using (MessageQueue queue = Open(path))
                Assert.That(queue.Receive(TimeSpan.FromSeconds(60)).Body, Is.EqualTo("from the peer"));
            Wait(peer);
            Assert.That(watch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(60)));
        }

        [Test]
        public void BeginReceiveSeesAnotherProcess()
        {
            string path = NewQueue();
            using MessageQueue queue = Open(path);
            IAsyncResult result = queue.BeginReceive(TimeSpan.FromSeconds(60));
            Peer("send", path, "async", "Normal", "-");
            Assert.That(result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(60)), Is.True);
            Assert.That(queue.EndReceive(result).Body, Is.EqualTo("async"));
        }

        [Test]
        public void WaitingThereForAMessageFromHere()
        {
            string path = NewQueue();
            Process peer = StartPeer("receive", path, "60000");
            System.Threading.Thread.Sleep(500);
            using (MessageQueue queue = Open(path))
                queue.Send("to the waiting peer");
            Assert.That(Wait(peer), Is.EqualTo(new[] { "to the waiting peer" }));
        }

        [Test]
        public void TransactionAcrossTwoQueues()
        {
            string a = NewQueue(transactional: true), b = NewQueue(transactional: true);
            Peer("send-transaction", a, b, "aborted", "abort");
            Peer("send-transaction", a, b, "committed", "commit");
            using MessageQueue queueA = Open(a), queueB = Open(b);
            Assert.That(queueA.GetAllMessages().Select(m => m.Body), Is.EqualTo(new[] { "committed" }));
            Assert.That(queueB.GetAllMessages().Select(m => m.Body), Is.EqualTo(new[] { "committed" }));
            Message fromA = queueA.Receive(TimeSpan.Zero, MessageQueueTransactionType.Single);
            Message fromB = queueB.Receive(TimeSpan.Zero, MessageQueueTransactionType.Single);
            Assert.That(fromA.TransactionId, Is.EqualTo(fromB.TransactionId));
        }

        [Test]
        public void TransactionHereSeenThere()
        {
            string a = NewQueue(transactional: true), b = NewQueue(transactional: true);
            using MessageQueue queueA = Open(a), queueB = Open(b);
            using (var transaction = new MessageQueueTransaction())
            {
                transaction.Begin();
                queueA.Send("a", transaction);
                queueB.Send("b", transaction);
                Assert.That(Peer("bodies", a), Is.Empty, "Hidden from the other process until commit");
                transaction.Commit();
            }
            Assert.That(Peer("bodies", a), Is.EqualTo(new[] { "a" }));
            Assert.That(Peer("bodies", b), Is.EqualTo(new[] { "b" }));
        }

        [Test]
        public void TransactionOfAnEndedProcessIsAborted()
        {
            string path = NewQueue(transactional: true);
            using (MessageQueue queue = Open(path))
                queue.Send("survives", MessageQueueTransactionType.Single);
            Assert.That(Peer("receive-and-end", path), Is.EqualTo(new[] { "survives" }));
            // The next process on this host to open the database aborts the ended process's transaction.
            Assert.That(Peer("bodies", path), Is.EqualTo(new[] { "survives" }));
        }
    }
}
