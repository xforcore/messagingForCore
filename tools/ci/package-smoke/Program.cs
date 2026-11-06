// Checks what an application gets from the messagingForCore package: Microsoft's System.Messaging with its .NET
// Framework identity, and, until the SQLite store exists (issue #1), the error .NET Framework gives where MSMQ isn't
// installed. Exits non-zero on any difference.
using System.Messaging;
using System.Reflection;

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

// Formatters and messages work without a queue.
var message = new Message("hello", new XmlMessageFormatter(new[] { typeof(string) }));
Check(message.Formatter is XmlMessageFormatter, "Message with an XmlMessageFormatter");

try
{
    MessageQueue.Exists(@".\private$\messagingforcore-smoke");
    Check(false, "MessageQueue.Exists without a queue store throws");
}
catch (InvalidOperationException e)
{
    Check(e.Message == "Message Queuing has not been installed on this computer.", $"MessageQueue.Exists: {e.Message}");
}

return failures == 0 ? 0 : 1;
