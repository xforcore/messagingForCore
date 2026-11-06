// Licensed under the MIT License. Copyright (c) messagingForCore contributors.
//
// The second process of the gate tests. Each command does one thing on the database in MESSAGINGFORCORE_DB and
// prints its result on standard output, one line per message.
//   send <queue> <body> [priority] [correlation id] [delay ms]   prints the message's ID and lookup ID
//   send-transaction <queue> <queue> <body> commit|abort         one message to each queue in one transaction
//   receive <queue> <timeout ms>                                 prints the body
//   receive-and-end <queue>                                      receives in a transaction and ends without
//                                                                committing (the process exits)
//   bodies <queue>                                               the bodies in the queue, in order
using System;
using System.Messaging;
using System.Threading;

public static class Program
{
    public static int Main(string[] args)
    {
        string command = args[0];
        using var queue = new MessageQueue(args[1]) { Formatter = new BinaryMessageFormatter() };
        queue.MessageReadPropertyFilter.SetAll();
        switch (command)
        {
            case "send":
            {
                if (args.Length > 5)
                    Thread.Sleep(int.Parse(args[5]));
                var message = new Message(args[2], new BinaryMessageFormatter());
                if (args.Length > 3)
                    message.Priority = Enum.Parse<MessagePriority>(args[3]);
                if (args.Length > 4 && args[4] != "-")
                    message.CorrelationId = args[4];
                queue.Send(message, queue.Transactional ? MessageQueueTransactionType.Single : MessageQueueTransactionType.None);
                Console.WriteLine(message.Id);
                return 0;
            }
            case "send-transaction":
            {
                using var other = new MessageQueue(args[2]) { Formatter = new BinaryMessageFormatter() };
                using var transaction = new MessageQueueTransaction();
                transaction.Begin();
                queue.Send(args[3], transaction);
                other.Send(args[3], transaction);
                if (args[4] == "commit")
                    transaction.Commit();
                else
                    transaction.Abort();
                return 0;
            }
            case "receive":
                Console.WriteLine(queue.Receive(TimeSpan.FromMilliseconds(int.Parse(args[2]))).Body);
                return 0;
            case "receive-and-end":
            {
                var transaction = new MessageQueueTransaction();
                transaction.Begin();
                Console.WriteLine(queue.Receive(TimeSpan.Zero, transaction).Body);
                Console.Out.Flush();
                // Ends with the transaction open, as an application that crashes does.
                Environment.Exit(0);
                return 0;
            }
            case "bodies":
                foreach (Message message in queue.GetAllMessages())
                    Console.WriteLine(message.Body);
                return 0;
            default:
                Console.Error.WriteLine("unknown command " + command);
                return 2;
        }
    }
}
