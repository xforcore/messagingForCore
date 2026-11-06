// Licensed under the MIT License. Copyright (c) messagingForCore contributors.
//
// Issue #1, "Setup tool": messagingforcore-msmq install, create-queue, list, purge, delete-queue and upgrade, on a
// database of their own.
using System;
using System.IO;
using System.Messaging;
using MessagingForCore.Msmq;
using NUnit.Framework;

namespace MessagingForCore.Tests
{
    [TestFixture]
    [NonParallelizable]
    public class ToolTests
    {
        private string path;

        [SetUp]
        public void NewPath() => path = System.IO.Path.Combine(TestDatabase.Folder, "tool-" + Guid.NewGuid().ToString("N") + ".db");

        [TearDown]
        public void UseTestDatabase() => TestDatabase.Use(TestDatabase.Path);

        private static string Run(int expected, params string[] args)
        {
            var output = new StringWriter();
            Assert.That(Program.Run(args, output), Is.EqualTo(expected), string.Join(" ", args) + ": " + output);
            return output.ToString();
        }

        [Test]
        public void Install()
        {
            StringAssert.Contains("schema version " + Schema.CurrentVersion, Run(0, "install", path));
            Assert.That(File.Exists(path), Is.True);
            Assert.That(Schema.Version(path), Is.EqualTo(Schema.CurrentVersion));
            StringAssert.Contains("already a queue database", Run(0, "install", path));
        }

        [Test]
        public void InstallOverAnotherFile()
        {
            File.WriteAllText(path, "");
            Assert.Throws<InvalidOperationException>(() => Run(1, "install", path));
        }

        [Test]
        public void QueueCommands()
        {
            Run(0, "install", path);
            StringAssert.Contains("transactional", Run(0, "create-queue", @".\private$\orders", "--transactional", "--label", "Orders", "--db", path));
            Run(0, "create-queue", @".\private$\audit", "--db", path);

            TestDatabase.Use(path);
            using (var orders = new MessageQueue(@".\private$\orders"))
            {
                Assert.That(orders.Transactional, Is.True);
                Assert.That(orders.Label, Is.EqualTo("Orders"));
                orders.Send("1", MessageQueueTransactionType.Single);
                orders.Send("2", MessageQueueTransactionType.Single);
            }

            string list = Run(0, "list", "--db", path);
            StringAssert.Contains(@".\private$\orders (transactional) ""Orders"": 2 messages", list);
            StringAssert.Contains(@".\private$\audit: 0 messages", list);
            StringAssert.Contains("2 queues", list);

            Run(0, "purge", @".\private$\orders", "--db", path);
            StringAssert.Contains(@"orders (transactional) ""Orders"": 0 messages", Run(0, "list", "--db", path));

            Run(0, "delete-queue", @".\private$\audit", "--db", path);
            StringAssert.Contains("1 queue in", Run(0, "list", "--db", path));
            Assert.That(MessageQueue.Exists(@".\private$\audit"), Is.False);
        }

        [Test]
        public void Upgrade()
        {
            Run(0, "install", path);
            StringAssert.Contains("nothing to do", Run(0, "upgrade", path));
        }

        [Test]
        public void Usage()
        {
            StringAssert.Contains("install <file>", Run(2));
        }
    }
}
