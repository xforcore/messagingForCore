// Licensed under the MIT License. Copyright (c) messagingForCore contributors.
using System;
using System.IO;
using System.Messaging;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace MessagingForCore.Tests
{
    /// <summary>A queue database for the run, prepared as messagingforcore-msmq install prepares one.</summary>
    [SetUpFixture]
    public class TestDatabase
    {
        internal const string AppContextKey = "MessagingForCore.Database";

        internal static string Folder { get; private set; }
        internal static string Path { get; private set; }

        [OneTimeSetUp]
        public void Install()
        {
            Folder = System.IO.Path.Combine(TestContext.CurrentContext.WorkDirectory, "queues-" + Guid.NewGuid().ToString("N"));
            Path = System.IO.Path.Combine(Folder, "queues.db");
            Assert.That(MessagingForCore.Msmq.Schema.Install(Path), Is.True);
            Use(Path);
        }

        [OneTimeTearDown]
        public void Remove()
        {
            Use(null);
            SqliteConnection.ClearAllPools();
            try
            {
                Directory.Delete(Folder, recursive: true);
            }
            catch (IOException)
            {
            }
        }

        internal static void Use(string path) => AppContext.SetData(AppContextKey, path);

        /// <summary>A new queue's path, .\private$\test-...</summary>
        internal static string NewQueuePath() => @".\private$\test-" + Guid.NewGuid().ToString("N");

        internal static MessageQueue NewQueue(bool transactional = false, IMessageFormatter formatter = null)
        {
            MessageQueue queue = MessageQueue.Create(NewQueuePath(), transactional);
            queue.Formatter = formatter ?? new BinaryMessageFormatter();
            return queue;
        }

        internal static MessageQueueErrorCode ErrorOf(TestDelegate code)
        {
            return Assert.Throws<MessageQueueException>(code).MessageQueueErrorCode;
        }
    }
}
