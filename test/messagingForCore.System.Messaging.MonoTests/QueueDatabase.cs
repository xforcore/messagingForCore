// Licensed under the MIT License. Copyright (c) messagingForCore contributors.
//
// Mono's tests ran against MSMQ (or RabbitMQ). Here they run against a queue database of their own, prepared once
// for the run as messagingforcore-msmq install prepares one, in the test output folder (removed afterwards). The
// AppContext value overrides MESSAGINGFORCORE_DB for this process only.
using System;
using System.IO;
using NUnit.Framework;

[SetUpFixture]
public class QueueDatabase
{
    private string folder;

    [OneTimeSetUp]
    public void Install()
    {
        folder = Path.Combine(TestContext.CurrentContext.WorkDirectory, "queues-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(folder, "queues.db");
        MessagingForCore.Msmq.Schema.Install(path);
        AppContext.SetData("MessagingForCore.Database", path);
    }

    [OneTimeTearDown]
    public void Remove()
    {
        AppContext.SetData("MessagingForCore.Database", null);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
