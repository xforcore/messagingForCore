// Licensed under the MIT License. Copyright (c) messagingForCore contributors.
//
// A queue's row and its properties (QUEUE_PROPID_*): read from a caller's properties for MQCreateQueue and
// MQSetQueueProperties, and written into them for MQGetQueueProperties, as MSMQ did. Strings and GUIDs the caller asks
// for with VT_NULL are allocated for the caller to free with MQFreeMemory. A private queue has no instance ID (that is
// a public queue's, from Active Directory): QUEUE_PROPID_INSTANCE stays VT_NULL, as MSMQ left it, and
// MessageQueue.Id reads Guid.Empty.
using System.Collections.Generic;
using System.Messaging.Interop;
using Microsoft.Data.Sqlite;

namespace System.Messaging.Store
{
    internal sealed class QueueRow
    {
        // MQ_PRIV_LEVEL_OPTIONAL, MSMQ's default.
        private const int PrivacyOptional = 1;
        private const long Infinite = 0xFFFFFFFFL;

        internal long Id;
        internal string Name;
        internal string Label = "";
        internal bool Transactional;
        internal bool Journal;
        internal long Quota = Infinite;
        internal long JournalQuota = Infinite;
        internal int BasePriority;
        internal Guid Category;
        internal bool Authenticate;
        internal int PrivacyLevel = PrivacyOptional;
        internal string MulticastAddress;
        internal long Created;
        internal long Modified;

        private const string Columns =
            "id, name, label, transactional, journal, quota, journal_quota, base_priority, category, authenticate, " +
            "privacy_level, multicast_address, created, modified";

        internal static QueueRow Find(SqliteConnection connection, QueueNames.QueueReference reference)
        {
            using (SqliteCommand command = connection.CreateCommand())
            {
                if (reference.Name != null)
                {
                    command.CommandText = "SELECT " + Columns + " FROM queues WHERE name = $name";
                    command.Parameters.AddWithValue("$name", reference.Name);
                }
                else
                {
                    command.CommandText = "SELECT " + Columns + " FROM queues WHERE id = $id";
                    command.Parameters.AddWithValue("$id", reference.Id);
                }
                using (SqliteDataReader reader = command.ExecuteReader())
                {
                    if (!reader.Read())
                        return null;
                    byte[] category = QueueStore.Blob(reader, 8);
                    return new QueueRow
                    {
                        Id = reader.GetInt64(0),
                        Name = reader.GetString(1),
                        Label = reader.GetString(2),
                        Transactional = reader.GetInt32(3) != 0,
                        Journal = reader.GetInt32(4) != 0,
                        Quota = reader.GetInt64(5),
                        JournalQuota = reader.GetInt64(6),
                        BasePriority = reader.GetInt32(7),
                        Category = category == null || category.Length != 16 ? Guid.Empty : new Guid(category),
                        Authenticate = reader.GetInt32(9) != 0,
                        PrivacyLevel = reader.GetInt32(10),
                        MulticastAddress = reader.IsDBNull(11) ? null : reader.GetString(11),
                        Created = reader.GetInt64(12),
                        Modified = reader.GetInt64(13),
                    };
                }
            }
        }

        internal static bool Exists(SqliteConnection connection, long id)
        {
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = "SELECT count(*) FROM queues WHERE id = $id";
                command.Parameters.AddWithValue("$id", id);
                return (long)command.ExecuteScalar() != 0;
            }
        }

        /// <summary>The path names of this machine's private queues (MQMgmtGetInfo's PROPID_MGMT_MSMQ_PRIVATEQ).</summary>
        internal static string[] PathNames(SqliteConnection connection)
        {
            var names = new List<string>();
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = "SELECT name FROM queues ORDER BY name";
                using (SqliteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                        names.Add(QueueNames.PathName(reader.GetString(0)));
                }
            }
            return names.ToArray();
        }

        /// <summary>Takes the properties a caller sets; a status for a property MSMQ wouldn't take.</summary>
        internal int Read(PropertyBlock block, bool creating)
        {
            for (int i = 0; i < block.Count; i++)
            {
                switch (block.Id(i))
                {
                    case NativeMethods.QUEUE_PROPID_PATHNAME:
                        if (!creating)
                            return (int)MessageQueueErrorCode.PropertyNotAllowed;
                        break;
                    case NativeMethods.QUEUE_PROPID_TRANSACTION:
                        // Whether a queue is transactional is chosen when it's created.
                        if (!creating)
                            return (int)MessageQueueErrorCode.PropertyNotAllowed;
                        Transactional = block.Integer(i) != NativeMethods.QUEUE_TRANSACTIONAL_NONE;
                        break;
                    case NativeMethods.QUEUE_PROPID_LABEL:
                        Label = block.String(i, -1) ?? "";
                        if (Label.Length > NativeMethods.MAX_LABEL_LEN)
                            return (int)MessageQueueErrorCode.IllegalPropertyValue;
                        break;
                    case NativeMethods.QUEUE_PROPID_JOURNAL:
                        Journal = block.Integer(i) != 0;
                        break;
                    case NativeMethods.QUEUE_PROPID_QUOTA:
                        Quota = block.Integer(i);
                        break;
                    case NativeMethods.QUEUE_PROPID_JOURNAL_QUOTA:
                        JournalQuota = block.Integer(i);
                        break;
                    case NativeMethods.QUEUE_PROPID_BASEPRIORITY:
                        BasePriority = (int)block.Integer(i);
                        break;
                    case NativeMethods.QUEUE_PROPID_TYPE:
                        Category = block.Guid(i);
                        break;
                    case NativeMethods.QUEUE_PROPID_AUTHENTICATE:
                        Authenticate = block.Integer(i) != 0;
                        break;
                    case NativeMethods.QUEUE_PROPID_PRIV_LEVEL:
                        PrivacyLevel = (int)block.Integer(i);
                        break;
                    case NativeMethods.QUEUE_PROPID_MULTICAST_ADDRESS:
                        string address = block.Vt(i) == PropertyBlock.VT_LPWSTR ? block.String(i, -1) : null;
                        MulticastAddress = string.IsNullOrEmpty(address) ? null : address;
                        break;
                    default:
                        return (int)MessageQueueErrorCode.PropertyNotAllowed;
                }
            }
            return 0;
        }

        /// <summary>Writes the properties a caller asks for.</summary>
        internal void Write(PropertyBlock block)
        {
            for (int i = 0; i < block.Count; i++)
            {
                bool allocate = block.Vt(i) == PropertyBlock.VT_NULL;
                switch (block.Id(i))
                {
                    case NativeMethods.QUEUE_PROPID_PATHNAME:
                        if (allocate)
                            block.AllocateString(i, QueueNames.PathName(Name));
                        break;
                    case NativeMethods.QUEUE_PROPID_LABEL:
                        if (allocate)
                            block.AllocateString(i, Label);
                        break;
                    case NativeMethods.QUEUE_PROPID_TYPE:
                        if (allocate)
                            block.AllocateGuid(i, Category);
                        else
                            block.WriteGuid(i, Category);
                        break;
                    case NativeMethods.QUEUE_PROPID_MULTICAST_ADDRESS:
                        if (allocate && MulticastAddress != null)
                            block.AllocateString(i, MulticastAddress);
                        break;
                    case NativeMethods.QUEUE_PROPID_TRANSACTION:
                        block.SetInteger(i, Transactional ? NativeMethods.QUEUE_TRANSACTIONAL_TRANSACTIONAL : NativeMethods.QUEUE_TRANSACTIONAL_NONE);
                        break;
                    case NativeMethods.QUEUE_PROPID_JOURNAL:
                        block.SetInteger(i, Journal ? 1 : 0);
                        break;
                    case NativeMethods.QUEUE_PROPID_QUOTA:
                        block.SetInteger(i, Quota);
                        break;
                    case NativeMethods.QUEUE_PROPID_JOURNAL_QUOTA:
                        block.SetInteger(i, JournalQuota);
                        break;
                    case NativeMethods.QUEUE_PROPID_BASEPRIORITY:
                        block.SetInteger(i, BasePriority);
                        break;
                    case NativeMethods.QUEUE_PROPID_AUTHENTICATE:
                        block.SetInteger(i, Authenticate ? 1 : 0);
                        break;
                    case NativeMethods.QUEUE_PROPID_PRIV_LEVEL:
                        block.SetInteger(i, PrivacyLevel);
                        break;
                    case NativeMethods.QUEUE_PROPID_CREATE_TIME:
                        block.SetInteger(i, Created);
                        break;
                    case NativeMethods.QUEUE_PROPID_MODIFY_TIME:
                        block.SetInteger(i, Modified);
                        break;
                }
            }
        }

        /// <summary>Inserts a new queue; false if one with the name exists.</summary>
        internal bool Insert(SqliteConnection connection)
        {
            Created = Modified = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText =
                    "INSERT INTO queues (name, label, transactional, journal, quota, journal_quota, base_priority, " +
                    "category, authenticate, privacy_level, multicast_address, created, modified) VALUES ($name, " +
                    "$label, $transactional, $journal, $quota, $journalQuota, $basePriority, $category, $authenticate, " +
                    "$privacyLevel, $multicast, $created, $modified) ON CONFLICT (name) DO NOTHING RETURNING id";
                Bind(command);
                command.Parameters.AddWithValue("$name", Name);
                command.Parameters.AddWithValue("$transactional", Transactional ? 1 : 0);
                command.Parameters.AddWithValue("$created", Created);
                object id = command.ExecuteScalar();
                if (id == null || id is DBNull)
                    return false;
                Id = (long)id;
                return true;
            }
        }

        internal void Update(SqliteConnection connection)
        {
            Modified = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText =
                    "UPDATE queues SET label = $label, journal = $journal, quota = $quota, journal_quota = $journalQuota, " +
                    "base_priority = $basePriority, category = $category, authenticate = $authenticate, " +
                    "privacy_level = $privacyLevel, multicast_address = $multicast, modified = $modified WHERE id = $id";
                Bind(command);
                command.Parameters.AddWithValue("$id", Id);
                command.ExecuteNonQuery();
            }
        }

        private void Bind(SqliteCommand command)
        {
            command.Parameters.AddWithValue("$label", Label);
            command.Parameters.AddWithValue("$journal", Journal ? 1 : 0);
            command.Parameters.AddWithValue("$quota", Quota);
            command.Parameters.AddWithValue("$journalQuota", JournalQuota);
            command.Parameters.AddWithValue("$basePriority", BasePriority);
            command.Parameters.AddWithValue("$category", Category == Guid.Empty ? (object)DBNull.Value : Category.ToByteArray());
            command.Parameters.AddWithValue("$authenticate", Authenticate ? 1 : 0);
            command.Parameters.AddWithValue("$privacyLevel", PrivacyLevel);
            command.Parameters.AddWithValue("$multicast", (object)MulticastAddress ?? DBNull.Value);
            command.Parameters.AddWithValue("$modified", Modified);
        }
    }
}
