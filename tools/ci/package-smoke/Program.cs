// Checks what an application gets from the messagingForCore package: Microsoft's System.Messaging with its .NET
// Framework identity, on the queue database the messagingForCore.Msmq tool prepared (MESSAGINGFORCORE_DB), and
// MSMQ's "service not available" without one. Exits non-zero on any difference.
using System.Messaging;

var failures = 0;
void Check(bool ok, string what)
{
    Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {what}");
    if (!ok) failures++;
}

var name = typeof(MessageQueue).Assembly.GetName();
Check(name.Name == "System.Messaging", $"assembly name {name.Name}");
Check(name.Version == new Version(4, 0, 0, 0), $"assembly version {name.Version}");
var token = Convert.ToHexString(name.GetPublicKeyToken() ?? []).ToLowerInvariant();
Check(token == "b03f5f7f11d50a3a", $"public key token {token}");

if (args.Length > 0 && args[0] == "no-database")
{
    try
    {
        MessageQueue.Exists(@".\private$\smoke");
        Check(false, "MessageQueue.Exists without a database throws");
    }
    catch (MessageQueueException e)
    {
        Check(e.MessageQueueErrorCode == MessageQueueErrorCode.ServiceNotAvailable && e.Message.Contains("MESSAGINGFORCORE_DB"),
              $"without a database: {e.MessageQueueErrorCode}");
    }
    return failures == 0 ? 0 : 1;
}

Check(MessageQueue.Exists(@".\private$\smoke"), "the queue the tool created exists");
using (var queue = new MessageQueue(@".\private$\smoke"))
{
    queue.Formatter = new BinaryMessageFormatter();
    using (var transaction = new MessageQueueTransaction())
    {
        transaction.Begin();
        queue.Send(new Message("binary") { Formatter = new BinaryMessageFormatter() }, transaction);
        queue.Send(new Message("xml") { Formatter = new XmlMessageFormatter() }, transaction);
        transaction.Commit();
    }
    Check((string)queue.Receive(TimeSpan.Zero, MessageQueueTransactionType.Single).Body == "binary", "BinaryMessageFormatter round trip");
    queue.Formatter = new XmlMessageFormatter(new[] { typeof(string) });
    Check((string)queue.Receive(TimeSpan.Zero, MessageQueueTransactionType.Single).Body == "xml", "XmlMessageFormatter round trip");
    queue.Send("left for the list", MessageQueueTransactionType.Single);
}

return failures == 0 ? 0 : 1;
