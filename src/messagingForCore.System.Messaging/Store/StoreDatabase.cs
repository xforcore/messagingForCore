// Licensed under the MIT License. Copyright (c) messagingForCore contributors.
//
// The queue database: one SQLite file per machine (or shared volume), prepared by the setup tool
// (messagingforcore-msmq install), as turning on the Windows feature prepared MSMQ. The store never creates or upgrades
// it: a missing setting, a missing file or a file without the schema it expects is MQ_ERROR_SERVICE_NOT_AVAILABLE, the
// error an application gets where MSMQ isn't running, with a message that names the setting and the tool. So an
// unmounted volume can't quietly become an empty queue store.
using System.IO;
using Microsoft.Data.Sqlite;

namespace System.Messaging.Store
{
    internal static class StoreDatabase
    {
        /// <summary>The environment variable that gives the database file's path.</summary>
        internal const string EnvironmentVariable = "MESSAGINGFORCORE_DB";

        /// <summary>
        /// An AppContext value (runtimeconfig.json's configProperties, or AppContext.SetData) that overrides the
        /// environment variable for one application, for tests.
        /// </summary>
        internal const string AppContextKey = "MessagingForCore.Database";

        /// <summary>The schema version this store reads and writes (the tool's Schema.CurrentVersion).</summary>
        internal const int SchemaVersion = 1;

        internal const string Tool = "messagingforcore-msmq";

        private static readonly object sync = new object();
        private static string confirmedPath;
        private static Guid confirmedMachineId;

        internal static string ConfiguredPath
        {
            get
            {
                string path = AppContext.GetData(AppContextKey) as string;
                if (string.IsNullOrEmpty(path))
                    path = Environment.GetEnvironmentVariable(EnvironmentVariable);
                return string.IsNullOrEmpty(path) ? null : Path.GetFullPath(path);
            }
        }

        /// <summary>
        /// Opens the configured database after confirming it, or throws MessageQueueException(ServiceNotAvailable).
        /// </summary>
        internal static SqliteConnection Open(out Guid machineId)
        {
            string path = ConfiguredPath;
            if (path == null)
                throw NotAvailable("messagingForCore's queue database isn't set: set " + EnvironmentVariable +
                                   " to the file that '" + Tool + " install <file>' prepared.");
            if (!File.Exists(path))
                throw NotAvailable("messagingForCore's queue database " + path + " (" + EnvironmentVariable +
                                   ") doesn't exist: prepare it with '" + Tool + " install " + path + "'.");

            SqliteConnection connection = new SqliteConnection(ConnectionString(path));
            try
            {
                connection.Open();
                lock (sync)
                {
                    if (confirmedPath == path)
                    {
                        machineId = confirmedMachineId;
                        return connection;
                    }
                }

                machineId = Confirm(connection, path);
                lock (sync)
                {
                    confirmedPath = path;
                    confirmedMachineId = machineId;
                }
                Recovery.AbortTransactionsOfEndedProcesses(connection);
                return connection;
            }
            catch (SqliteException e)
            {
                connection.Dispose();
                throw NotAvailable("messagingForCore's queue database " + path + " can't be used (" + e.Message +
                                   "): prepare it with '" + Tool + " install " + path + "'.");
            }
            catch
            {
                connection.Dispose();
                throw;
            }
        }

        internal static string ConnectionString(string path)
        {
            return new SqliteConnectionStringBuilder
            {
                DataSource = path,
                // Never create the file: the setup tool does (see above).
                Mode = SqliteOpenMode.ReadWrite,
                ForeignKeys = true,
                Pooling = true,
                // Seconds a command waits for another connection's write lock (SQLITE_BUSY) before failing.
                DefaultTimeout = 30,
            }.ToString();
        }

        private static Guid Confirm(SqliteConnection connection, string path)
        {
            string version = null, machine = null;
            try
            {
                using (SqliteCommand command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT key, value FROM meta WHERE key IN ('schema_version', 'machine_id')";
                    using (SqliteDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            if (reader.GetString(0) == "schema_version")
                                version = reader.GetString(1);
                            else
                                machine = reader.GetString(1);
                        }
                    }
                }
            }
            catch (SqliteException)
            {
                version = null;
            }

            if (version == null || machine == null || !Guid.TryParse(machine, out Guid machineId))
                throw NotAvailable("messagingForCore's queue database " + path + " (" + EnvironmentVariable +
                                   ") has no queue schema: prepare it with '" + Tool + " install " + path + "'.");
            if (version != SchemaVersion.ToString(Globalization.CultureInfo.InvariantCulture))
                throw NotAvailable("messagingForCore's queue database " + path + " has schema version " + version +
                                   ", and this messagingForCore uses version " + SchemaVersion + ": run '" + Tool +
                                   " upgrade " + path + "' with the tool of this messagingForCore version.");
            return machineId;
        }

        /// <summary>Forgets the confirmed database (tests that switch databases, and the tool after an upgrade).</summary>
        internal static void Reset()
        {
            lock (sync)
                confirmedPath = null;
            Recovery.Reset();
        }

        private static MessageQueueException NotAvailable(string limitation)
        {
            return new MessageQueueException((int)MessageQueueErrorCode.ServiceNotAvailable, limitation);
        }
    }
}
