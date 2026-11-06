// Licensed under the MIT License. Copyright (c) messagingForCore contributors.
//
// Issue #1, "Formatters": XmlMessageFormatter and BinaryMessageFormatter (on webForCore's port of BinaryFormatter,
// since .NET's throws). ActiveXMessageFormatter's primitive types need no COM, so they work too; COM objects don't.
using System;
using System.Messaging;
using NUnit.Framework;

namespace MessagingForCore.Tests
{
    [Serializable]
    public class Order
    {
        public int Number { get; set; }
        public string Customer { get; set; }
        public decimal Total { get; set; }
        public DateTime Placed { get; set; }
        public string[] Lines { get; set; }
    }

    [TestFixture]
    public class FormatterTests
    {
        private static Order NewOrder() => new Order
        {
            Number = 1234,
            Customer = "Contoso",
            Total = 99.95m,
            Placed = new DateTime(2026, 11, 6, 12, 30, 0, DateTimeKind.Utc),
            Lines = new[] { "widget", "gadget" },
        };

        private static void AssertOrder(Order order)
        {
            Assert.That(order.Number, Is.EqualTo(1234));
            Assert.That(order.Customer, Is.EqualTo("Contoso"));
            Assert.That(order.Total, Is.EqualTo(99.95m));
            Assert.That(order.Placed, Is.EqualTo(new DateTime(2026, 11, 6, 12, 30, 0, DateTimeKind.Utc)));
            Assert.That(order.Lines, Is.EqualTo(new[] { "widget", "gadget" }));
        }

        [Test]
        public void Xml()
        {
            using var queue = TestDatabase.NewQueue(formatter: new XmlMessageFormatter(new[] { typeof(Order) }));
            queue.Send(NewOrder());
            AssertOrder((Order)queue.Receive(TimeSpan.Zero).Body);
        }

        [Test]
        public void XmlIsTheDefault()
        {
            using var queue = TestDatabase.NewQueue();
            queue.Formatter = new XmlMessageFormatter(new[] { "System.String,mscorlib" });
            using var sender = new MessageQueue(queue.Path);
            sender.Send("text");
            Message received = queue.Receive(TimeSpan.Zero);
            Assert.That(new System.IO.StreamReader(received.BodyStream).ReadToEnd(), Does.Contain("<string>text</string>"));
            received.BodyStream.Position = 0;
            Assert.That(received.Body, Is.EqualTo("text"));
        }

        [Test]
        public void Binary()
        {
            using var queue = TestDatabase.NewQueue(formatter: new BinaryMessageFormatter());
            queue.Send(NewOrder());
            Message received = queue.Receive(TimeSpan.Zero);
            Assert.That(received.BodyType, Is.EqualTo(0x300));
            AssertOrder((Order)received.Body);
        }

        [Test]
        public void ActiveXPrimitives()
        {
            using var queue = TestDatabase.NewQueue(formatter: new ActiveXMessageFormatter());
            object[] values = { "text", 42, 3.5, true, (byte)7, 12345678901L, 2.5m, new DateTime(2026, 11, 6), new byte[] { 1, 2 } };
            // A Message each: queue.Send(object) reuses DefaultPropertiesToSend's message, and on .NET Framework too a
            // byte[] sent with ActiveXMessageFormatter after a value written as a stream takes that stream's bytes.
            foreach (object value in values)
                queue.Send(new Message(value, new ActiveXMessageFormatter()));
            foreach (object value in values)
                Assert.That(queue.Receive(TimeSpan.Zero).Body, Is.EqualTo(value), value.GetType().Name);
        }
    }
}
