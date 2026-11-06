// Licensed under the MIT License. Copyright (c) messagingForCore contributors.
using System;
using System.Collections.Generic;
using System.Linq;

namespace MessagingForCore.Comparison
{
    /// <summary>An application's message type: public properties for XmlSerializer, [Serializable] for BinaryFormatter.</summary>
    [Serializable]
    public class Order
    {
        public int Number { get; set; }
        public string Customer { get; set; }
        public decimal Total { get; set; }
        public DateTime Placed { get; set; }
        public List<string> Lines { get; set; }
        public Status State { get; set; }

        public static Order Sample() => new Order
        {
            Number = 1234,
            Customer = "Contoso, Grüße",
            Total = 99.95m,
            Placed = new DateTime(2026, 11, 6, 12, 30, 15, DateTimeKind.Utc),
            Lines = new List<string> { "widget", "gadget" },
            State = Status.Shipped,
        };

        public override bool Equals(object obj) =>
            obj is Order o && o.Number == Number && o.Customer == Customer && o.Total == Total && o.Placed == Placed &&
            o.State == State && (o.Lines ?? new List<string>()).SequenceEqual(Lines ?? new List<string>());

        public override int GetHashCode() => Number;

        public override string ToString() => "Order " + Number + " (" + Customer + ", " + Total + ", " + Placed.ToString("o") + ", " +
                                              string.Join("/", Lines ?? new List<string>()) + ", " + State + ")";
    }

    public enum Status
    {
        New,
        Shipped,
    }
}
