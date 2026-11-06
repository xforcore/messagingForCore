// Licensed under the MIT License. Copyright (c) messagingForCore contributors.
//
// messagingforcore-msmq: what turning on Windows' Message Queuing feature and Computer Management's queue pages did,
// for messagingForCore's queue database. install and upgrade prepare the file; create-queue, delete-queue, purge and
// list are for operations, because many applications expect their queues to exist. The queue commands go through
// System.Messaging itself (MessageQueue.Create, Delete, Purge), so they behave as an application's calls do.
using System;
using System.Collections.Generic;
using System.IO;
using System.Messaging;
using Microsoft.Data.Sqlite;

namespace MessagingForCore.Msmq
{
    public static class Program
    {
        private const string EnvironmentVariable = "MESSAGINGFORCORE_DB";
        private const string AppContextKey = "MessagingForCore.Database";

        private const string Usage = @"messagingforcore-msmq: prepares and manages messagingForCore's queue database.

  messagingforcore-msmq install <file>        create the database (WAL mode, schema, machine ID)
  messagingforcore-msmq upgrade <file>        bring the database to this version's schema
  messagingforcore-msmq create-queue <path> [--transactional] [--label <label>]
  messagingforcore-msmq delete-queue <path>
  messagingforcore-msmq purge <path>          remove a queue's messages
  messagingforcore-msmq list                  queues and their message counts

<path> is a private queue's path, .\private$\<name>. The queue commands use the database in --db <file>, or else in
the MESSAGINGFORCORE_DB environment variable, as applications do. Keep the file on a local disk (not NFS or SMB); in a
container, mount its folder (WAL mode keeps -wal and -shm files beside it).";

        public static int Main(string[] args)
        {
            try
            {
                return Run(args, Console.Out);
            }
            catch (MessageQueueException e)
            {
                Console.Error.WriteLine("messagingforcore-msmq: " + e.Message + " (" + e.MessageQueueErrorCode + ")");
                return 1;
            }
            catch (Exception e) when (e is InvalidOperationException || e is ArgumentException || e is SqliteException || e is IOException)
            {
                Console.Error.WriteLine("messagingforcore-msmq: " + e.Message);
                return 1;
            }
        }

        public static int Run(string[] args, TextWriter output)
        {
            var positional = new List<string>();
            bool transactional = false;
            string? label = null, database = null;
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--transactional":
                        transactional = true;
                        break;
                    case "--label" when i + 1 < args.Length:
                        label = args[++i];
                        break;
                    case "--db" when i + 1 < args.Length:
                        database = args[++i];
                        break;
                    case "-h":
                    case "--help":
                        output.WriteLine(Usage);
                        return 0;
                    default:
                        positional.Add(args[i]);
                        break;
                }
            }
            if (positional.Count == 0)
            {
                output.WriteLine(Usage);
                return 2;
            }

            string command = positional[0];
            string? argument = positional.Count > 1 ? positional[1] : null;
            if (database != null)
                AppContext.SetData(AppContextKey, Path.GetFullPath(database));

            switch (command)
            {
                case "install" when argument != null:
                    if (!Schema.Install(argument))
                    {
                        int? version = Schema.Version(argument);
                        if (version == null)
                            throw new InvalidOperationException(argument + " exists and isn't a queue database; remove it or choose another file.");
                        output.WriteLine(Path.GetFullPath(argument) + " is already a queue database (schema version " + version + ").");
                        return 0;
                    }
                    output.WriteLine("Created " + Path.GetFullPath(argument) + " (schema version " + Schema.CurrentVersion + "). Set " +
                                     EnvironmentVariable + " to it.");
                    return 0;

                case "upgrade" when argument != null:
                    int from = Schema.Upgrade(argument);
                    output.WriteLine(from == Schema.CurrentVersion
                        ? Path.GetFullPath(argument) + " is at schema version " + from + "; nothing to do."
                        : Path.GetFullPath(argument) + " upgraded from schema version " + from + " to " + Schema.CurrentVersion + ".");
                    return 0;

                case "create-queue" when argument != null:
                    using (MessageQueue queue = MessageQueue.Create(argument, transactional))
                    {
                        if (label != null)
                            queue.Label = label;
                    }
                    output.WriteLine("Created " + argument + (transactional ? " (transactional)." : "."));
                    return 0;

                case "delete-queue" when argument != null:
                    MessageQueue.Delete(argument);
                    output.WriteLine("Deleted " + argument + ".");
                    return 0;

                case "purge" when argument != null:
                    using (MessageQueue queue = new MessageQueue(argument))
                        queue.Purge();
                    output.WriteLine("Purged " + argument + ".");
                    return 0;

                case "list":
                    List(output, database);
                    return 0;

                default:
                    output.WriteLine(Usage);
                    return 2;
            }
        }

        private static void List(TextWriter output, string? database)
        {
            string? path = database ?? Environment.GetEnvironmentVariable(EnvironmentVariable);
            if (string.IsNullOrEmpty(path))
                throw new InvalidOperationException("No database: give --db <file> or set " + EnvironmentVariable + ".");
            path = Path.GetFullPath(path);
            int? version = File.Exists(path) ? Schema.Version(path) : null;
            if (version != Schema.CurrentVersion)
                throw new InvalidOperationException(path + " isn't a queue database of schema version " + Schema.CurrentVersion +
                                                    ": run 'messagingforcore-msmq install' or 'upgrade'.");

            using var connection = new SqliteConnection(Schema.ConnectionString(path, create: false));
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT q.name, q.transactional, q.label, " +
                "(SELECT count(*) FROM messages m WHERE m.queue_id = q.id AND m.state = 0), " +
                "(SELECT count(*) FROM messages m WHERE m.queue_id = q.id AND m.state <> 0) " +
                "FROM queues q ORDER BY q.name";
            using var reader = command.ExecuteReader();
            int queues = 0;
            while (reader.Read())
            {
                queues++;
                string pending = reader.GetInt64(4) == 0 ? "" : ", " + reader.GetInt64(4) + " in open transactions";
                string queueLabel = reader.GetString(2).Length == 0 ? "" : " \"" + reader.GetString(2) + "\"";
                output.WriteLine(@".\private$\" + reader.GetString(0) + (reader.GetInt64(1) != 0 ? " (transactional)" : "") +
                                 queueLabel + ": " + reader.GetInt64(3) + " messages" + pending);
            }
            output.WriteLine(queues + (queues == 1 ? " queue" : " queues") + " in " + path + ".");
        }
    }
}
