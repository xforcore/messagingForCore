// Licensed under the MIT License. Copyright (c) messagingForCore contributors.
//
// Issue #1, "Confirmed": without a prepared database every call fails with MQ_ERROR_SERVICE_NOT_AVAILABLE, the error
// an application gets where MSMQ isn't running, naming the setting and the tool; the store never creates the file.
using System;
using System.IO;
using System.Messaging;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace MessagingForCore.Tests
{
    [TestFixture]
    [NonParallelizable]
    public class DatabaseTests
    {
        [TearDown]
        public void UseTestDatabase() => TestDatabase.Use(TestDatabase.Path);

        private static MessageQueueException Unavailable()
        {
            var e = Assert.Throws<MessageQueueException>(() => MessageQueue.Exists(TestDatabase.NewQueuePath()));
            Assert.That(e.MessageQueueErrorCode, Is.EqualTo(MessageQueueErrorCode.ServiceNotAvailable));
            return e;
        }

        [Test]
        public void NoSetting()
        {
            string saved = Environment.GetEnvironmentVariable("MESSAGINGFORCORE_DB");
            Environment.SetEnvironmentVariable("MESSAGINGFORCORE_DB", null);
            try
            {
                TestDatabase.Use(null);
                StringAssert.Contains("MESSAGINGFORCORE_DB", Unavailable().Message);
            }
            finally
            {
                Environment.SetEnvironmentVariable("MESSAGINGFORCORE_DB", saved);
            }
        }

        [Test]
        public void EnvironmentVariable()
        {
            string saved = Environment.GetEnvironmentVariable("MESSAGINGFORCORE_DB");
            try
            {
                TestDatabase.Use(null);
                Environment.SetEnvironmentVariable("MESSAGINGFORCORE_DB", TestDatabase.Path);
                Assert.That(MessageQueue.Exists(TestDatabase.NewQueuePath()), Is.False);
            }
            finally
            {
                Environment.SetEnvironmentVariable("MESSAGINGFORCORE_DB", saved);
            }
        }

        [Test]
        public void MissingFile()
        {
            string path = System.IO.Path.Combine(TestDatabase.Folder, "missing-" + Guid.NewGuid().ToString("N") + ".db");
            TestDatabase.Use(path);
            var e = Unavailable();
            StringAssert.Contains("messagingforcore-msmq install", e.Message);
            Assert.That(File.Exists(path), Is.False, "The store mustn't create the database");
        }

        [Test]
        public void FileWithoutSchema()
        {
            string path = System.IO.Path.Combine(TestDatabase.Folder, "empty-" + Guid.NewGuid().ToString("N") + ".db");
            using (var connection = new SqliteConnection("Data Source=" + path))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE other (x INTEGER)";
                command.ExecuteNonQuery();
            }
            TestDatabase.Use(path);
            StringAssert.Contains("no queue schema", Unavailable().Message);
        }

        [Test]
        public void NotADatabase()
        {
            string path = System.IO.Path.Combine(TestDatabase.Folder, "text-" + Guid.NewGuid().ToString("N") + ".db");
            File.WriteAllText(path, "not a database, just text that is long enough to have a header's worth of bytes");
            TestDatabase.Use(path);
            Unavailable();
        }

        [Test]
        public void OtherSchemaVersion()
        {
            string path = System.IO.Path.Combine(TestDatabase.Folder, "v99-" + Guid.NewGuid().ToString("N") + ".db");
            MessagingForCore.Msmq.Schema.Install(path);
            using (var connection = new SqliteConnection("Data Source=" + path + ";Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "UPDATE meta SET value = '99' WHERE key = 'schema_version'";
                command.ExecuteNonQuery();
            }
            TestDatabase.Use(path);
            StringAssert.Contains("upgrade", Unavailable().Message);
        }

        [Test]
        public void EachDatabaseIsAMachine()
        {
            // Two databases are two machines: separate queues, separate machine IDs.
            string other = System.IO.Path.Combine(TestDatabase.Folder, "other-" + Guid.NewGuid().ToString("N") + ".db");
            MessagingForCore.Msmq.Schema.Install(other);
            string path = TestDatabase.NewQueuePath();
            MessageQueue.Create(path).Dispose();
            Guid first = MessageQueue.GetMachineId(".");

            TestDatabase.Use(other);
            Assert.That(MessageQueue.Exists(path), Is.False);
            Assert.That(MessageQueue.GetMachineId("."), Is.Not.EqualTo(first));
        }
    }
}
