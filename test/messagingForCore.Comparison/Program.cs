// Licensed under the MIT License. Copyright (c) messagingForCore contributors.
//
// Message bodies across .NET Framework 4.8 and messagingForCore.
//   write <folder>         writes each case's body (<case>.body) and body type (<case>.type)
//   read <folder>          reads each case's body back with its formatter and checks the value
//   compare <a> <b>        says which cases' bytes are identical (for information: formatters may differ in bytes
//                          and still read each other)
// Built for net48 it uses .NET Framework's System.Messaging, with no queue (formatters need none). Built for net10.0
// it uses messagingForCore's, and sends every body through a queue in a database of its own first.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Messaging;
using MessagingForCore.Comparison;

public static class Program
{
    private sealed class Case
    {
        public string Name;
        public Func<IMessageFormatter> Formatter;
        public object Value;
    }

    private static IMessageFormatter Xml() => new XmlMessageFormatter(new[] { typeof(string), typeof(int), typeof(Order) });

    private static readonly Case[] Cases =
    {
        new Case { Name = "xml-string", Formatter = Xml, Value = "Grüße, 東京" },
        new Case { Name = "xml-int", Formatter = Xml, Value = 42 },
        new Case { Name = "xml-order", Formatter = Xml, Value = Order.Sample() },
        new Case { Name = "binary-string", Formatter = () => new BinaryMessageFormatter(), Value = "Grüße, 東京" },
        new Case { Name = "binary-int", Formatter = () => new BinaryMessageFormatter(), Value = 42 },
        new Case { Name = "binary-decimal", Formatter = () => new BinaryMessageFormatter(), Value = 99.95m },
        new Case { Name = "binary-datetime", Formatter = () => new BinaryMessageFormatter(), Value = new DateTime(2026, 11, 6, 12, 30, 15, DateTimeKind.Utc) },
        new Case { Name = "binary-guid", Formatter = () => new BinaryMessageFormatter(), Value = new Guid("0f8fad5b-d9cb-469f-a165-70867728950e") },
        new Case { Name = "binary-bytes", Formatter = () => new BinaryMessageFormatter(), Value = new byte[] { 0, 1, 2, 254, 255 } },
        new Case { Name = "binary-list", Formatter = () => new BinaryMessageFormatter(), Value = new List<int> { 1, 2, 3 } },
        new Case { Name = "binary-dictionary", Formatter = () => new BinaryMessageFormatter(), Value = new Dictionary<string, int> { { "a", 1 }, { "b", 2 } } },
        new Case { Name = "binary-order", Formatter = () => new BinaryMessageFormatter(), Value = Order.Sample() },
    };

    public static int Main(string[] args)
    {
        Console.WriteLine("System.Messaging " + typeof(MessageQueue).Assembly.GetName().Version + " on " +
                          System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
        switch (args.Length > 0 ? args[0] : "")
        {
            case "write":
                Directory.CreateDirectory(args[1]);
                foreach (Case c in Cases)
                {
                    var message = new Message { Formatter = c.Formatter() };
                    message.Formatter.Write(message, c.Value);
                    byte[] body = Transport(ReadAll(message.BodyStream), message.BodyType, out int bodyType);
                    File.WriteAllBytes(Path.Combine(args[1], c.Name + ".body"), body);
                    File.WriteAllText(Path.Combine(args[1], c.Name + ".type"), bodyType.ToString());
                    Console.WriteLine("wrote " + c.Name + " (" + body.Length + " bytes, body type " + bodyType + ")");
                }
                return 0;

            case "read":
                int failures = 0;
                foreach (Case c in Cases)
                {
                    string path = Path.Combine(args[1], c.Name + ".body");
                    if (!File.Exists(path))
                    {
                        Console.WriteLine("FAIL " + c.Name + ": no " + path);
                        failures++;
                        continue;
                    }
                    byte[] body = Transport(File.ReadAllBytes(path), int.Parse(File.ReadAllText(Path.Combine(args[1], c.Name + ".type"))), out int bodyType);
                    var message = new Message { Formatter = c.Formatter(), BodyStream = new MemoryStream(body), BodyType = bodyType };
                    object value;
                    try
                    {
                        value = message.Formatter.Read(message);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("FAIL " + c.Name + ": " + e.GetType().Name + ": " + e.Message);
                        failures++;
                        continue;
                    }
                    bool same = Same(c.Value, value);
                    Console.WriteLine((same ? "ok   " : "FAIL ") + c.Name + ": " + Show(value));
                    if (!same)
                        failures++;
                }
                Console.WriteLine(failures == 0 ? "All bodies read." : failures + " bodies didn't read.");
                return failures == 0 ? 0 : 1;

            case "compare":
                foreach (Case c in Cases)
                {
                    byte[] a = File.ReadAllBytes(Path.Combine(args[1], c.Name + ".body"));
                    byte[] b = File.ReadAllBytes(Path.Combine(args[2], c.Name + ".body"));
                    Console.WriteLine(c.Name + ": " + (a.SequenceEqual(b) ? "identical bytes" : "different bytes (" + a.Length + " and " + b.Length + ")"));
                }
                return 0;

            default:
                Console.Error.WriteLine("usage: write <folder> | read <folder> | compare <folder> <folder>");
                return 2;
        }
    }

    private static byte[] ReadAll(Stream stream)
    {
        var copy = new MemoryStream();
        stream.Position = 0;
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    private static bool Same(object expected, object actual)
    {
        if (expected is System.Collections.IDictionary e && actual is System.Collections.IDictionary a)
            return e.Count == a.Count && e.Keys.Cast<object>().All(k => a.Contains(k) && Equals(e[k], a[k]));
        if (expected is System.Collections.IEnumerable s && !(expected is string) && actual is System.Collections.IEnumerable t)
            return s.Cast<object>().SequenceEqual(t.Cast<object>());
        return Equals(expected, actual) && (expected == null || expected.GetType() == actual.GetType());
    }

    private static string Show(object value) =>
        value is System.Collections.IEnumerable e && !(value is string) ? value.GetType().Name + " [" + string.Join(", ", e.Cast<object>()) + "]" : value + " (" + value.GetType().Name + ")";

#if NETFRAMEWORK
    // .NET Framework: the formatters only; no MSMQ on the machine is needed or used.
    private static byte[] Transport(byte[] body, int bodyType, out int receivedType)
    {
        receivedType = bodyType;
        return body;
    }
#else
    private static string queuePath;

    // messagingForCore: the body goes through a queue in a database of this run's own, and comes back as received.
    private static byte[] Transport(byte[] body, int bodyType, out int receivedType)
    {
        if (queuePath == null)
        {
            string database = Path.Combine(AppContext.BaseDirectory, "comparison-" + Guid.NewGuid().ToString("N"), "queues.db");
            MessagingForCore.Msmq.Schema.Install(database);
            AppContext.SetData("MessagingForCore.Database", database);
            queuePath = @".\private$\comparison";
            MessageQueue.Create(queuePath).Dispose();
        }
        using (var queue = new MessageQueue(queuePath))
        {
            queue.Send(new Message { BodyStream = new MemoryStream(body), BodyType = bodyType });
            Message received = queue.Receive(TimeSpan.Zero);
            receivedType = received.BodyType;
            return ReadAll(received.BodyStream);
        }
    }
#endif
}
