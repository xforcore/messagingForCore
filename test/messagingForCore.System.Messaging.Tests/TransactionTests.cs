// Licensed under the MIT License. Copyright (c) messagingForCore contributors.
//
// Issue #1, "Transactions": MessageQueueTransaction across several queues, committed and aborted, and
// MessageQueueTransactionType.Single; MSMQ's transaction usage rules for local queues; Automatic inside a
// System.Transactions scope, which isn't supported.
using System;
using System.Linq;
using System.Messaging;
using System.Transactions;
using NUnit.Framework;

namespace MessagingForCore.Tests
{
    [TestFixture]
    public class TransactionTests
    {
        private static bool IsEmpty(MessageQueue queue) => queue.GetAllMessages().Length == 0;

        [Test]
        public void SendsAreHiddenUntilCommit()
        {
            using var a = TestDatabase.NewQueue(transactional: true);
            using var b = TestDatabase.NewQueue(transactional: true);
            using (var transaction = new MessageQueueTransaction())
            {
                transaction.Begin();
                a.Send("to a", transaction);
                b.Send("to b", transaction);
                Assert.That(IsEmpty(a) && IsEmpty(b), Is.True, "Not visible before commit");
                transaction.Commit();
                Assert.That(transaction.Status, Is.EqualTo(MessageQueueTransactionStatus.Committed));
            }
            a.MessageReadPropertyFilter.SetAll();
            b.MessageReadPropertyFilter.SetAll();
            Message fromA = a.Receive(TimeSpan.Zero, MessageQueueTransactionType.Single);
            Message fromB = b.Receive(TimeSpan.Zero, MessageQueueTransactionType.Single);
            Assert.That(fromA.Body, Is.EqualTo("to a"));
            Assert.That(fromB.Body, Is.EqualTo("to b"));
            Assert.That(fromA.TransactionId, Is.EqualTo(fromB.TransactionId));
            Assert.That(fromA.IsFirstInTransaction && !fromA.IsLastInTransaction, Is.True);
            Assert.That(!fromB.IsFirstInTransaction && fromB.IsLastInTransaction, Is.True);
        }

        [Test]
        public void AbortDiscardsSends()
        {
            using var a = TestDatabase.NewQueue(transactional: true);
            using var b = TestDatabase.NewQueue(transactional: true);
            using (var transaction = new MessageQueueTransaction())
            {
                transaction.Begin();
                a.Send("to a", transaction);
                b.Send("to b", transaction);
                transaction.Abort();
            }
            Assert.That(IsEmpty(a) && IsEmpty(b), Is.True);
        }

        [Test]
        public void ReceiveAndSendInOneTransaction()
        {
            using var input = TestDatabase.NewQueue(transactional: true);
            using var output = TestDatabase.NewQueue(transactional: true);
            input.Send("work", MessageQueueTransactionType.Single);

            using (var transaction = new MessageQueueTransaction())
            {
                transaction.Begin();
                Assert.That(input.Receive(TimeSpan.Zero, transaction).Body, Is.EqualTo("work"));
                output.Send("result", transaction);
                Assert.That(IsEmpty(input), Is.True, "Received in an open transaction: hidden");
                transaction.Abort();
            }
            Assert.That(input.GetAllMessages().Select(m => m.Body), Is.EqualTo(new[] { "work" }), "Abort returns the message");
            Assert.That(IsEmpty(output), Is.True);

            using (var transaction = new MessageQueueTransaction())
            {
                transaction.Begin();
                input.Receive(TimeSpan.Zero, transaction);
                output.Send("result", transaction);
                transaction.Commit();
            }
            Assert.That(IsEmpty(input), Is.True);
            Assert.That(output.Receive(TimeSpan.Zero, MessageQueueTransactionType.Single).Body, Is.EqualTo("result"));
        }

        [Test]
        public void DisposeAbortsAnOpenTransaction()
        {
            using var queue = TestDatabase.NewQueue(transactional: true);
            using (var transaction = new MessageQueueTransaction())
            {
                transaction.Begin();
                queue.Send("x", transaction);
            }
            Assert.That(IsEmpty(queue), Is.True);
        }

        [Test]
        public void TransactionalMessagesKeepTheirOrder()
        {
            using var queue = TestDatabase.NewQueue(transactional: true);
            queue.Send(new Message("low", new BinaryMessageFormatter()) { Priority = MessagePriority.Low }, MessageQueueTransactionType.Single);
            queue.Send(new Message("high", new BinaryMessageFormatter()) { Priority = MessagePriority.Highest }, MessageQueueTransactionType.Single);
            Assert.That(queue.GetAllMessages().Select(m => m.Body), Is.EqualTo(new[] { "low", "high" }));
        }

        [Test]
        public void TransactionUsage()
        {
            using var transactional = TestDatabase.NewQueue(transactional: true);
            using var plain = TestDatabase.NewQueue();
            Assert.That(TestDatabase.ErrorOf(() => transactional.Send("x")), Is.EqualTo(MessageQueueErrorCode.TransactionUsage));
            Assert.That(TestDatabase.ErrorOf(() => plain.Send("x", MessageQueueTransactionType.Single)), Is.EqualTo(MessageQueueErrorCode.TransactionUsage));
            plain.Send("x");
            Assert.That(TestDatabase.ErrorOf(() => plain.Receive(TimeSpan.Zero, MessageQueueTransactionType.Single)), Is.EqualTo(MessageQueueErrorCode.TransactionUsage));
            Assert.That(plain.Receive(TimeSpan.Zero).Body, Is.EqualTo("x"));
            // A non-transactional receive from a transactional queue is allowed.
            transactional.Send("y", MessageQueueTransactionType.Single);
            Assert.That(transactional.Receive(TimeSpan.Zero).Body, Is.EqualTo("y"));
        }

        [Test]
        public void AutomaticOutsideAScopeIsNoTransaction()
        {
            // MSMQ gave a message no transaction when there was no COM+ or System.Transactions transaction.
            using var queue = TestDatabase.NewQueue();
            queue.Send("x", MessageQueueTransactionType.Automatic);
            Assert.That(queue.Receive(TimeSpan.Zero, MessageQueueTransactionType.Automatic).Body, Is.EqualTo("x"));
        }

        [Test]
        public void AutomaticInATransactionScope()
        {
            using var queue = TestDatabase.NewQueue(transactional: true);
            using (new TransactionScope())
            {
                var e = Assert.Throws<MessageQueueException>(() => queue.Send("x", MessageQueueTransactionType.Automatic));
                Assert.That(e.MessageQueueErrorCode, Is.EqualTo(MessageQueueErrorCode.TransactionUsage));
                StringAssert.Contains("Automatic", e.Message);
            }
        }
    }
}
