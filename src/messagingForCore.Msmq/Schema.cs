// Licensed under the MIT License. Copyright (c) messagingForCore contributors.
//
// The queue database's schema, which only this tool writes (System.Messaging checks the version and never creates
// or upgrades it). One file per machine or shared volume, in WAL mode so that readers and the writer don't block each
// other; keep it on a local disk, not NFS or SMB, where SQLite's locking is unreliable.
using System;
using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;

namespace MessagingForCore.Msmq
{
    public static class Schema
    {
        /// <summary>The version this tool writes; System.Messaging's StoreDatabase.SchemaVersion must match it.</summary>
        public const int CurrentVersion = 1;

        // Version 1.
        private const string Version1 = @"
CREATE TABLE meta (
    key TEXT PRIMARY KEY,
    value TEXT NOT NULL
);

-- Private queues: QUEUE_PROPID_* (MessageQueue's properties). The name is the part after private$\.
CREATE TABLE queues (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL UNIQUE COLLATE NOCASE,
    label TEXT NOT NULL DEFAULT '',
    transactional INTEGER NOT NULL DEFAULT 0,
    journal INTEGER NOT NULL DEFAULT 0,
    quota INTEGER NOT NULL DEFAULT 4294967295,
    journal_quota INTEGER NOT NULL DEFAULT 4294967295,
    base_priority INTEGER NOT NULL DEFAULT 0,
    category BLOB,
    authenticate INTEGER NOT NULL DEFAULT 0,
    privacy_level INTEGER NOT NULL DEFAULT 1,
    multicast_address TEXT,
    created INTEGER NOT NULL,
    modified INTEGER NOT NULL
);

-- Messages, read by priority (highest first), then lookup_id (MSMQ's LookupId, in order of arrival).
-- state: 0 in the queue; 1 sent in a transaction that hasn't committed; 2 received in one that hasn't ended.
-- properties: the other properties as sent (acknowledgement, journal, time limits, ...), kept but not acted on.
CREATE TABLE messages (
    lookup_id INTEGER PRIMARY KEY AUTOINCREMENT,
    queue_id INTEGER NOT NULL REFERENCES queues (id) ON DELETE CASCADE,
    message_id BLOB NOT NULL,
    correlation_id BLOB,
    label TEXT,
    body BLOB,
    body_type INTEGER NOT NULL DEFAULT 0,
    priority INTEGER NOT NULL DEFAULT 3,
    app_specific INTEGER NOT NULL DEFAULT 0,
    extension BLOB,
    response_queue TEXT,
    sent INTEGER NOT NULL,
    arrived INTEGER NOT NULL,
    transaction_id BLOB,
    first_in_transaction INTEGER NOT NULL DEFAULT 0,
    last_in_transaction INTEGER NOT NULL DEFAULT 0,
    properties BLOB,
    state INTEGER NOT NULL DEFAULT 0,
    transaction_owner INTEGER
);
CREATE INDEX messages_order ON messages (queue_id, state, priority DESC, lookup_id);
CREATE INDEX messages_owner ON messages (transaction_owner) WHERE transaction_owner IS NOT NULL;

-- Open MessageQueueTransactions, by host and process, so that a process that ended can be found and its
-- transactions aborted.
CREATE TABLE transactions (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    host TEXT NOT NULL,
    process_id INTEGER NOT NULL,
    started INTEGER NOT NULL
);
";

        public static string ConnectionString(string path, bool create)
        {
            return new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = create ? SqliteOpenMode.ReadWriteCreate : SqliteOpenMode.ReadWrite,
                ForeignKeys = true,
                Pooling = false,
            }.ToString();
        }

        /// <summary>The schema version in a database file, or null if it has none.</summary>
        public static int? Version(string path)
        {
            using var connection = new SqliteConnection(ConnectionString(path, create: false));
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name = 'meta'";
            if ((long)command.ExecuteScalar()! == 0)
                return null;
            command.CommandText = "SELECT value FROM meta WHERE key = 'schema_version'";
            object? value = command.ExecuteScalar();
            return value == null ? null : int.Parse((string)value, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Creates a database at <paramref name="path"/>: the file, WAL mode, the schema and its version, and a new
        /// machine ID (the GUID in this machine's message IDs and PRIVATE= format names). False if the file exists.
        /// </summary>
        public static bool Install(string path)
        {
            path = Path.GetFullPath(path);
            if (File.Exists(path))
                return false;
            string? folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder))
                Directory.CreateDirectory(folder);

            using var connection = new SqliteConnection(ConnectionString(path, create: true));
            connection.Open();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA journal_mode = WAL";
                command.ExecuteNonQuery();
            }
            using var transaction = connection.BeginTransaction();
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = Version1 +
                    "INSERT INTO meta (key, value) VALUES ('schema_version', $version), ('created', $created), " +
                    "('machine_id', $machine);";
                command.Parameters.AddWithValue("$version", CurrentVersion.ToString(CultureInfo.InvariantCulture));
                command.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture));
                command.Parameters.AddWithValue("$machine", Guid.NewGuid().ToString("D"));
                command.ExecuteNonQuery();
            }
            transaction.Commit();
            return true;
        }

        /// <summary>
        /// Brings a database to <see cref="CurrentVersion"/>. Returns the version it had. Version 1 is the first, so
        /// there is nothing to migrate yet; a later version adds its steps here.
        /// </summary>
        public static int Upgrade(string path)
        {
            int? version = Version(path);
            if (version == null)
                throw new InvalidOperationException(path + " has no queue schema: run 'messagingforcore-msmq install " + path + "'.");
            if (version > CurrentVersion)
                throw new InvalidOperationException(path + " has schema version " + version + ", newer than this tool's " +
                                                    CurrentVersion + ": use the tool of the messagingForCore version that wrote it.");
            return version.Value;
        }
    }
}
