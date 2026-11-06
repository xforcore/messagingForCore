// Licensed under the MIT License. Copyright (c) messagingForCore contributors.
//
// Internal transactions (MessageQueueTransaction, MessageQueueTransactionType.Single). A MessageQueueTransaction can
// stay open a long time, and a SQLite write transaction held that long would block every queue in the file, so a
// queue transaction is a row in the transactions table and marks on messages instead, each written in a short SQLite
// transaction:
// - a message sent in the transaction is stored at once but hidden (state 1) until commit;
// - a message received in the transaction is hidden (state 2) until commit deletes it or abort makes it visible again.
// Commit and abort are each one SQLite transaction, so they apply to every queue the transaction touched at once.
// A transaction left open by a process that ended is aborted the next time a process on the same host opens the
// database (Recovery), as MSMQ aborted it when the application ended.
using System.Diagnostics;
using System.Messaging.Interop;
using Microsoft.Data.Sqlite;

namespace System.Messaging.Store
{
    internal sealed class StoreTransaction : ITransaction
    {
        // XACT_E_NOTRANSACTION: the transaction was already committed or aborted.
        private const int NoTransaction = unchecked((int)0x8004D00E);
        // E_NOTIMPL
        private const int NotImplemented = unchecked((int)0x80004001);

        private readonly object sync = new object();
        private bool ended;

        internal StoreTransaction(long id, byte[] transactionId)
        {
            Id = id;
            TransactionId = transactionId;
        }

        /// <summary>The transactions table's row.</summary>
        internal long Id { get; }

        /// <summary>MSMQ's transaction ID (PROPID_M_XACTID), on every message sent in the transaction.</summary>
        internal byte[] TransactionId { get; }

        /// <summary>Whether a message was sent in the transaction (the first one is FIRST_IN_XACT).</summary>
        internal bool Sent { get; set; }

        internal bool Ended
        {
            get { lock (sync) return ended; }
        }

        /// <summary>Starts a transaction: its row in the transactions table.</summary>
        internal static StoreTransaction Begin(SqliteConnection connection, Guid machineId)
        {
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText =
                    "INSERT INTO transactions (host, process_id, started) VALUES ($host, $pid, $started) RETURNING id";
                command.Parameters.AddWithValue("$host", Environment.MachineName);
                command.Parameters.AddWithValue("$pid", Environment.ProcessId);
                command.Parameters.AddWithValue("$started", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                long id = (long)command.ExecuteScalar();
                Recovery.Open(connection.DataSource, id);
                return new StoreTransaction(id, MessageProperties.MakeId(machineId, id));
            }
        }

        int ITransaction.Commit(int fRetaining, int grfTC, int grfRM)
        {
            return End(commit: true);
        }

        int ITransaction.Abort(int pboidReason, int fRetaining, int fAsync)
        {
            return End(commit: false);
        }

        int ITransaction.GetTransactionInfo(IntPtr pinfo)
        {
            return NotImplemented;
        }

        private int End(bool commit)
        {
            lock (sync)
            {
                if (ended)
                    return NoTransaction;
                using (SqliteConnection connection = StoreDatabase.Open(out _))
                {
                    End(connection, Id, commit);
                    Recovery.Closed(connection.DataSource, Id);
                }
                ended = true;
            }
            Arrivals.Signal();
            return 0;
        }

        /// <summary>Commits or aborts a transaction's messages, in every queue, and removes its row.</summary>
        internal static void End(SqliteConnection connection, long id, bool commit)
        {
            using (SqliteTransaction transaction = connection.BeginTransaction(deferred: false))
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.Parameters.AddWithValue("$id", id);
                command.CommandText = commit
                    ? "UPDATE messages SET last_in_transaction = 1 WHERE lookup_id = " +
                      "(SELECT max(lookup_id) FROM messages WHERE transaction_owner = $id AND state = 1); " +
                      "UPDATE messages SET state = 0, transaction_owner = NULL WHERE transaction_owner = $id AND state = 1; " +
                      "DELETE FROM messages WHERE transaction_owner = $id AND state = 2; " +
                      "DELETE FROM transactions WHERE id = $id;"
                    : "DELETE FROM messages WHERE transaction_owner = $id AND state = 1; " +
                      "UPDATE messages SET state = 0, transaction_owner = NULL WHERE transaction_owner = $id AND state = 2; " +
                      "DELETE FROM transactions WHERE id = $id;";
                command.ExecuteNonQuery();
                transaction.Commit();
            }
        }
    }

    /// <summary>Aborts the transactions of processes on this host that have ended (once per process and database).</summary>
    internal static class Recovery
    {
        private static readonly object sync = new object();
        private static string recoveredPath;

        internal static void AbortTransactionsOfEndedProcesses(SqliteConnection connection)
        {
            lock (sync)
            {
                if (recoveredPath == connection.DataSource)
                    return;
                recoveredPath = connection.DataSource;
            }

            var ended = new System.Collections.Generic.List<long>();
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = "SELECT id, process_id FROM transactions WHERE host = $host";
                command.Parameters.AddWithValue("$host", Environment.MachineName);
                using (SqliteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (!IsOpen(connection.DataSource, reader.GetInt64(0), reader.GetInt32(1)))
                            ended.Add(reader.GetInt64(0));
                    }
                }
            }
            foreach (long id in ended)
                StoreTransaction.End(connection, id, commit: false);
            if (ended.Count > 0)
                Arrivals.Signal();
        }

        internal static void Reset()
        {
            lock (sync)
                recoveredPath = null;
        }

        // This process's open transactions, by database and row: a row with this process's ID that isn't here is a
        // previous process's (the ID was reused).
        private static readonly System.Collections.Generic.HashSet<string> open = new System.Collections.Generic.HashSet<string>();

        internal static void Open(string database, long id)
        {
            lock (sync)
                open.Add(database + "|" + id);
        }

        internal static void Closed(string database, long id)
        {
            lock (sync)
                open.Remove(database + "|" + id);
        }

        private static bool IsOpen(string database, long id, int processId)
        {
            if (processId == Environment.ProcessId)
            {
                lock (sync)
                    return open.Contains(database + "|" + id);
            }
            try
            {
                using (Process process = Process.GetProcessById(processId))
                    return !process.HasExited;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }
}
