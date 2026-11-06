// Licensed under the MIT License. Copyright (c) messagingForCore contributors.
//
// Issue #1, "Queues" and "Queue functions": private queues on this machine, by path name and format name; create,
// delete, exists, purge; queue properties. And the queues messagingForCore doesn't have, which fail naming the
// limitation.
using System;
using System.Linq;
using System.Messaging;
using NUnit.Framework;

namespace MessagingForCore.Tests
{
    [TestFixture]
    public class QueueTests
    {
        [Test]
        public void CreateExistsDelete()
        {
            string path = TestDatabase.NewQueuePath();
            Assert.That(MessageQueue.Exists(path), Is.False);
            using (MessageQueue queue = MessageQueue.Create(path))
                Assert.That(queue.Transactional, Is.False);
            Assert.That(MessageQueue.Exists(path), Is.True);
            Assert.That(MessageQueue.Exists(path.ToUpperInvariant()), Is.True, "Queue names are case-insensitive");
            MessageQueue.Delete(path);
            Assert.That(MessageQueue.Exists(path), Is.False);
        }

        [Test]
        public void CreateTwice()
        {
            string path = TestDatabase.NewQueuePath();
            MessageQueue.Create(path).Dispose();
            Assert.That(TestDatabase.ErrorOf(() => MessageQueue.Create(path)), Is.EqualTo(MessageQueueErrorCode.QueueExists));
        }

        [Test]
        public void DeleteMissing()
        {
            Assert.That(TestDatabase.ErrorOf(() => MessageQueue.Delete(TestDatabase.NewQueuePath())),
                        Is.EqualTo(MessageQueueErrorCode.QueueNotFound));
        }

        [Test]
        public void SendToMissing()
        {
            using var queue = new MessageQueue(TestDatabase.NewQueuePath());
            Assert.That(TestDatabase.ErrorOf(() => queue.Send("x")), Is.EqualTo(MessageQueueErrorCode.QueueNotFound));
        }

        [Test]
        public void PathNames()
        {
            string name = "test-" + Guid.NewGuid().ToString("N");
            MessageQueue.Create(@".\private$\" + name).Dispose();
            foreach (string machine in new[] { Environment.MachineName, "localhost", Environment.MachineName.ToLowerInvariant() })
                Assert.That(MessageQueue.Exists(machine + @"\private$\" + name), Is.True, machine);
        }

        [Test]
        public void FormatNames()
        {
            string name = "test-" + Guid.NewGuid().ToString("N");
            using var created = MessageQueue.Create(@".\private$\" + name);
            created.Send("by path");

            // MQPathNameToFormatName's PRIVATE= format name, with this database's machine ID.
            Guid machine = MessageQueue.GetMachineId(".");
            StringAssert.StartsWith("PRIVATE=" + machine.ToString("D") + @"\", created.FormatName);

            foreach (string formatName in new[]
            {
                created.FormatName,
                @"DIRECT=OS:.\private$\" + name,
                @"DIRECT=OS:localhost\private$\" + name,
                @"DIRECT=OS:" + Environment.MachineName + @"\private$\" + name,
            })
            {
                using var queue = new MessageQueue("FormatName:" + formatName) { Formatter = new XmlMessageFormatter(new[] { typeof(string) }) };
                queue.Send(formatName);
                Assert.That(queue.Peek(TimeSpan.Zero).Body, Is.EqualTo("by path"), formatName);
            }
            Assert.That(created.GetAllMessages().Length, Is.EqualTo(5));
        }

        [Test]
        public void Purge()
        {
            using var queue = TestDatabase.NewQueue();
            queue.Send("1");
            queue.Send("2");
            queue.Purge();
            Assert.That(TestDatabase.ErrorOf(() => queue.Receive(TimeSpan.Zero)), Is.EqualTo(MessageQueueErrorCode.IOTimeout));
        }

        [Test]
        public void Properties()
        {
            using var queue = TestDatabase.NewQueue(transactional: true);
            Guid category = Guid.NewGuid();
            queue.Label = "Orders to ship";
            queue.Category = category;
            queue.UseJournalQueue = true;
            queue.MaximumQueueSize = 2048;
            queue.MaximumJournalSize = 1024;
            queue.BasePriority = 2;

            // A new instance reads them from the store.
            using var again = new MessageQueue(queue.Path);
            Assert.That(again.Label, Is.EqualTo("Orders to ship"));
            Assert.That(again.Category, Is.EqualTo(category));
            Assert.That(again.UseJournalQueue, Is.True);
            Assert.That(again.MaximumQueueSize, Is.EqualTo(2048));
            Assert.That(again.MaximumJournalSize, Is.EqualTo(1024));
            Assert.That(again.BasePriority, Is.EqualTo(2));
            Assert.That(again.Transactional, Is.True);
            Assert.That(again.Authenticate, Is.False);
            Assert.That(again.EncryptionRequired, Is.EqualTo(EncryptionRequired.Optional));
            Assert.That(again.CreateTime, Is.EqualTo(DateTime.Now).Within(TimeSpan.FromMinutes(2)));
            Assert.That(again.LastModifyTime, Is.GreaterThanOrEqualTo(again.CreateTime));
            // A private queue has no instance ID (that is a public queue's, from Active Directory).
            Assert.That(again.Id, Is.EqualTo(Guid.Empty));
        }

        [Test]
        public void DefaultProperties()
        {
            using var queue = TestDatabase.NewQueue();
            using var again = new MessageQueue(queue.Path);
            Assert.That(again.Label, Is.EqualTo(""));
            Assert.That(again.Category, Is.EqualTo(Guid.Empty));
            Assert.That(again.UseJournalQueue, Is.False);
            Assert.That(again.MaximumQueueSize, Is.EqualTo(MessageQueue.InfiniteQueueSize));
        }

        [Test]
        public void QueueByFormatNameReadsItsPath()
        {
            // QUEUE_PROPID_PATHNAME, which MSMQ gave with this machine's name.
            using var queue = TestDatabase.NewQueue();
            string name = queue.Path.Substring(queue.Path.LastIndexOf('\\') + 1);
            using var byFormatName = new MessageQueue("FormatName:" + queue.FormatName);
            Assert.That(byFormatName.QueueName, Is.EqualTo(@"private$\" + name).IgnoreCase);
            Assert.That(byFormatName.MachineName, Is.EqualTo(Environment.MachineName).IgnoreCase);
        }

        [Test]
        public void PrivateQueuesByMachine()
        {
            using var queue = TestDatabase.NewQueue();
            string name = queue.Path.Substring(queue.Path.LastIndexOf('\\') + 1);
            MessageQueue[] queues = MessageQueue.GetPrivateQueuesByMachine(".");
            Assert.That(queues.Select(q => q.QueueName), Has.Some.EqualTo(@"private$\" + name).IgnoreCase);
            MessageQueue found = queues.First(q => string.Equals(q.QueueName, @"private$\" + name, StringComparison.OrdinalIgnoreCase));
            found.Formatter = new BinaryMessageFormatter();
            found.Send("found");
            Assert.That(queue.Receive(TimeSpan.Zero).Body, Is.EqualTo("found"));
        }

        [Test]
        public void MachineId()
        {
            Assert.That(MessageQueue.GetMachineId("."), Is.Not.EqualTo(Guid.Empty));
            Assert.That(MessageQueue.GetMachineId(Environment.MachineName), Is.EqualTo(MessageQueue.GetMachineId(".")));
        }

        // Not supported (issue #1): each fails with a MessageQueueException that names the limitation.

        private static void Unsupported(TestDelegate code, string limitation)
        {
            var e = Assert.Throws<MessageQueueException>(code);
            StringAssert.Contains("messagingForCore", e.Message);
            StringAssert.Contains(limitation, e.Message);
        }

        [Test]
        public void RemoteQueues()
        {
            Unsupported(() => MessageQueue.Exists(@"OTHERMACHINE\private$\orders"), "other computers");
            using var direct = new MessageQueue(@"FormatName:DIRECT=OS:OTHERMACHINE\private$\orders");
            Unsupported(() => direct.Send("x"), "other computers");
        }

        [Test]
        public void PublicQueues()
        {
            Unsupported(() => MessageQueue.Create(@".\orders"), "public queues");
            Unsupported(() => MessageQueue.GetPublicQueues(), "Active Directory");
        }

        [Test]
        public void OtherProtocols()
        {
            using var tcp = new MessageQueue(@"FormatName:DIRECT=TCP:127.0.0.1\private$\orders");
            Unsupported(() => tcp.Send("x"), "DIRECT=TCP");
            using var http = new MessageQueue(@"FormatName:DIRECT=HTTP://localhost/msmq/private$/orders");
            Unsupported(() => http.Send("x"), "DIRECT=TCP:, DIRECT=HTTP(S)");
        }

        [Test]
        public void SystemQueues()
        {
            using var queue = TestDatabase.NewQueue();
            using var journal = new MessageQueue("FormatName:" + queue.FormatName + ";JOURNAL");
            Unsupported(() => journal.Peek(TimeSpan.Zero), "journal");
        }

        [Test]
        public void Security()
        {
            using var queue = TestDatabase.NewQueue();
            Unsupported(() => queue.ResetPermissions(), "queue security");
            // SetPermissions: Microsoft's own check (AccessControlList.CheckEnvironment) refuses it off Windows before
            // MSMQ is called; on Windows it reaches the store, which refuses it.
            Exception e = Assert.Catch(() => queue.SetPermissions("Everyone", MessageQueueAccessRights.FullControl));
            if (OperatingSystem.IsWindows())
                StringAssert.Contains("queue security", e.Message);
            else
                Assert.That(e, Is.InstanceOf<PlatformNotSupportedException>());
        }
    }
}
