// Licensed under the MIT License. Copyright (c) messagingForCore contributors.
//
// The queue store: what MSMQ's runtime (mqrt.dll) did for System.Messaging, for local private queues kept in a SQLite
// database (StoreDatabase). Microsoft's interop layer (Interop/UnsafeNativeMethods.cs, Interop/SafeNativeMethods.cs)
// calls these functions where it called MSMQ's, with the same arguments, and gets MSMQ's status codes back, so
// Microsoft's System.Messaging above it is unchanged. What messagingForCore doesn't support throws a
// MessageQueueException that names the limitation (Limitations).
//
// Queue and cursor handles are numbers in this process's handle tables. Messages are read in MSMQ's order: priority,
// highest first, then arrival (lookup_id). A receive that finds no message waits for one until its timeout (Arrivals).
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Messaging.Interop;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace System.Messaging.Store
{
    internal static class QueueStore
    {
        // MQ_INFORMATION_OPERATION_PENDING: an asynchronous receive was started; the callback gives its result.
        private const int OperationPending = 0x400E0006;
        private const uint InfiniteTimeout = 0xFFFFFFFF;

        private const int ActionReceive = NativeMethods.QUEUE_ACTION_RECEIVE;
        private const int ActionPeekCurrent = NativeMethods.QUEUE_ACTION_PEEK_CURRENT;
        private const int ActionPeekNext = NativeMethods.QUEUE_ACTION_PEEK_NEXT;
        private const int LookupPeek = NativeMethods.LOOKUP_PEEK_MASK;
        private const int LookupReceive = NativeMethods.LOOKUP_RECEIVE_MASK;

        // MessageQueueTransactionType, as Microsoft's code passes it in place of a transaction.
        private const int TransactionNone = 0;
        private const int TransactionAutomatic = 1;
        private const int TransactionSingle = 3;

        private const string MessageColumns =
            "lookup_id, message_id, correlation_id, label, body, body_type, priority, app_specific, extension, " +
            "response_queue, sent, arrived, transaction_id, first_in_transaction, last_in_transaction, properties";

        // MSMQ's order: priority, highest first, then arrival.
        private const string QueueOrder = " ORDER BY priority DESC, lookup_id LIMIT 1";
        private const string ReverseOrder = " ORDER BY priority ASC, lookup_id DESC LIMIT 1";

        private sealed class QueueHandle
        {
            internal long Id;
            internal string FormatName;
            internal Guid MachineId;
            internal volatile bool Closed;
        }

        private sealed class QueueCursor
        {
            internal QueueHandle Queue;
            // Where the cursor is: before the first message (not positioned), at a message (Priority, LookupId), or,
            // after a receive through it, at the message after that one (Advance), as MSMQ moved it.
            internal bool Positioned;
            internal bool Advance;
            internal long Priority;
            internal long LookupId;
            internal volatile bool Closed;
        }

        private static long nextHandle;
        private static readonly ConcurrentDictionary<IntPtr, QueueHandle> queues = new ConcurrentDictionary<IntPtr, QueueHandle>();
        private static readonly ConcurrentDictionary<IntPtr, QueueCursor> cursors = new ConcurrentDictionary<IntPtr, QueueCursor>();

        private static IntPtr NewHandle() => new IntPtr(Interlocked.Increment(ref nextHandle));

        private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // Queues.

        public static int OpenQueue(string formatName, int access, int shareMode, out MessageQueueHandle handle)
        {
            handle = MessageQueueHandle.InvalidHandle;
            using (SqliteConnection connection = StoreDatabase.Open(out Guid machineId))
            {
                int status = QueueNames.ParseFormatName(formatName, machineId, out QueueNames.QueueReference reference);
                if (status != 0)
                    return status;
                QueueRow queue = QueueRow.Find(connection, reference);
                if (queue == null)
                    return (int)MessageQueueErrorCode.QueueNotFound;

                IntPtr value = NewHandle();
                queues[value] = new QueueHandle
                {
                    Id = queue.Id,
                    FormatName = QueueNames.FormatName(machineId, queue.Id),
                    MachineId = machineId,
                };
                handle = MessageQueueHandle.FromStore(value);
                return 0;
            }
        }

        public static int CloseQueue(IntPtr handle)
        {
            if (queues.TryRemove(handle, out QueueHandle queue))
            {
                // Pending receives on it end with OperationCanceled, as MSMQ cancelled them.
                queue.Closed = true;
                Arrivals.Signal();
            }
            return 0;
        }

        public static int CreateQueue(IntPtr securityDescriptor, MessagePropertyVariants.MQPROPS properties, StringBuilder formatName, ref int formatNameLength)
        {
            PropertyBlock block = new PropertyBlock(properties);
            int path = block.Find(NativeMethods.QUEUE_PROPID_PATHNAME);
            if (path < 0)
                return (int)MessageQueueErrorCode.InsufficientProperties;
            int status = QueueNames.ParsePathName(block.String(path, -1), out string name);
            if (status != 0)
                return status;

            QueueRow queue = new QueueRow { Name = name };
            status = queue.Read(block, creating: true);
            if (status != 0)
                return status;

            using (SqliteConnection connection = StoreDatabase.Open(out Guid machineId))
            {
                if (!queue.Insert(connection))
                    return (int)MessageQueueErrorCode.QueueExists;
                string result = QueueNames.FormatName(machineId, queue.Id);
                formatName.Clear().Append(result);
                formatNameLength = result.Length + 1;
            }
            return 0;
        }

        public static int DeleteQueue(string formatName)
        {
            using (SqliteConnection connection = StoreDatabase.Open(out Guid machineId))
            {
                int status = QueueNames.ParseFormatName(formatName, machineId, out QueueNames.QueueReference reference);
                if (status != 0)
                    return status;
                QueueRow queue = QueueRow.Find(connection, reference);
                if (queue == null)
                    return (int)MessageQueueErrorCode.QueueNotFound;
                using (SqliteCommand command = connection.CreateCommand())
                {
                    // Its messages go with it (ON DELETE CASCADE).
                    command.CommandText = "DELETE FROM queues WHERE id = $id";
                    command.Parameters.AddWithValue("$id", queue.Id);
                    command.ExecuteNonQuery();
                }
            }
            Arrivals.Signal();
            return 0;
        }

        public static int PathNameToFormatName(string pathName, StringBuilder formatName, ref int count)
        {
            int status = QueueNames.ParsePathName(pathName, out string name);
            if (status != 0)
                return status;
            using (SqliteConnection connection = StoreDatabase.Open(out Guid machineId))
            {
                QueueRow queue = QueueRow.Find(connection, new QueueNames.QueueReference { Name = name });
                if (queue == null)
                    return (int)MessageQueueErrorCode.QueueNotFound;
                string result = QueueNames.FormatName(machineId, queue.Id);
                formatName.Clear().Append(result);
                count = result.Length + 1;
            }
            return 0;
        }

        public static int GetQueueProperties(string formatName, MessagePropertyVariants.MQPROPS properties)
        {
            using (SqliteConnection connection = StoreDatabase.Open(out Guid machineId))
            {
                int status = QueueNames.ParseFormatName(formatName, machineId, out QueueNames.QueueReference reference);
                if (status != 0)
                    return status;
                QueueRow queue = QueueRow.Find(connection, reference);
                if (queue == null)
                    return (int)MessageQueueErrorCode.QueueNotFound;
                queue.Write(new PropertyBlock(properties));
            }
            return 0;
        }

        public static int SetQueueProperties(string formatName, MessagePropertyVariants.MQPROPS properties)
        {
            using (SqliteConnection connection = StoreDatabase.Open(out Guid machineId))
            {
                int status = QueueNames.ParseFormatName(formatName, machineId, out QueueNames.QueueReference reference);
                if (status != 0)
                    return status;
                QueueRow queue = QueueRow.Find(connection, reference);
                if (queue == null)
                    return (int)MessageQueueErrorCode.QueueNotFound;
                status = queue.Read(new PropertyBlock(properties), creating: false);
                if (status != 0)
                    return status;
                queue.Update(connection);
            }
            return 0;
        }

        public static int PurgeQueue(MessageQueueHandle handle)
        {
            if (!queues.TryGetValue(handle.DangerousGetHandle(), out QueueHandle queue))
                return (int)MessageQueueErrorCode.InvalidHandle;
            using (SqliteConnection connection = StoreDatabase.Open(out _))
            {
                if (!QueueRow.Exists(connection, queue.Id))
                    return (int)MessageQueueErrorCode.QueueDeleted;
                using (SqliteCommand command = connection.CreateCommand())
                {
                    // The queue's messages; not those sent in a transaction that hasn't committed (they haven't
                    // arrived) or received in one that hasn't ended (the transaction decides).
                    command.CommandText = "DELETE FROM messages WHERE queue_id = $queue AND state = 0";
                    command.Parameters.AddWithValue("$queue", queue.Id);
                    command.ExecuteNonQuery();
                }
            }
            return 0;
        }

        // Machine and management information.

        public static int GetMachineProperties(string machineName, IntPtr machineIdPointer, MessagePropertyVariants.MQPROPS properties)
        {
            using (SqliteConnection connection = StoreDatabase.Open(out Guid machineId))
            {
                if (machineIdPointer != IntPtr.Zero)
                {
                    if (PropertyBlock.GuidAt(machineIdPointer) != machineId)
                        return (int)MessageQueueErrorCode.MachineNotFound;
                }
                else if (machineName != null && !QueueNames.IsLocalMachine(machineName))
                    throw Limitations.RemoteQueue(machineName);

                PropertyBlock block = new PropertyBlock(properties);
                for (int i = 0; i < block.Count; i++)
                {
                    if (block.Vt(i) != PropertyBlock.VT_NULL)
                        continue;
                    if (block.Id(i) == NativeMethods.MACHINE_ID)
                        block.AllocateGuid(i, machineId);
                    else if (block.Id(i) == NativeMethods.MACHINE_PATHNAME)
                        block.AllocateString(i, Environment.MachineName);
                }
            }
            return 0;
        }

        public static int MgmtGetInfo(string machineName, string objectName, MessagePropertyVariants.MQPROPS properties)
        {
            if (!string.Equals(objectName, "MACHINE", StringComparison.OrdinalIgnoreCase))
                throw Limitations.Management(objectName);
            if (machineName != null && !QueueNames.IsLocalMachine(machineName))
                throw Limitations.RemoteQueue(machineName);

            using (SqliteConnection connection = StoreDatabase.Open(out _))
            {
                PropertyBlock block = new PropertyBlock(properties);
                for (int i = 0; i < block.Count; i++)
                {
                    if (block.Id(i) == NativeMethods.MANAGEMENT_PRIVATEQ)
                        block.AllocateStrings(i, QueueRow.PathNames(connection));
                    else
                        throw Limitations.Management(objectName);
                }
            }
            return 0;
        }

        public static void FreeMemory(IntPtr memory)
        {
            PropertyBlock.Free(memory);
        }

        // What isn't supported.

        public static int LocateBegin(out LocatorHandle handle)
        {
            handle = LocatorHandle.InvalidHandle;
            throw Limitations.ActiveDirectory();
        }

        public static int LocateNext()
        {
            throw Limitations.ActiveDirectory();
        }

        public static int InstanceToFormatName()
        {
            throw Limitations.ActiveDirectory();
        }

        public static int GetQueueSecurity(out int lengthNeeded)
        {
            lengthNeeded = 0;
            throw Limitations.Security();
        }

        public static int SetQueueSecurity()
        {
            throw Limitations.Security();
        }

        public static int GetSecurityContext(out SecurityContextHandle handle)
        {
            handle = null;
            throw Limitations.Security();
        }

        public static Exception AutomaticTransactionNotSupported() => Limitations.AutomaticTransaction();

        public static Exception ComObjectsNotSupported() => Limitations.ComObjects();

        // Transactions.

        public static int BeginTransaction(out ITransaction transaction)
        {
            using (SqliteConnection connection = StoreDatabase.Open(out Guid machineId))
                transaction = StoreTransaction.Begin(connection, machineId);
            return 0;
        }

        /// <summary>
        /// The transaction an operation runs in: an internal transaction, Single (its own one-message transaction), or
        /// none. Automatic outside a transaction scope is none: MSMQ gave a message no transaction when there was no
        /// COM+ context (inside a System.Transactions scope, Microsoft's code throws before it gets here).
        /// </summary>
        private static int Transaction(IntPtr type, ITransaction transaction, out StoreTransaction internalTransaction, out bool single)
        {
            internalTransaction = null;
            single = false;
            if (transaction != null)
            {
                internalTransaction = transaction as StoreTransaction;
                if (internalTransaction == null)
                    throw Limitations.ExternalTransaction();
                if (internalTransaction.Ended)
                    return (int)MessageQueueErrorCode.TransactionUsage;
                return 0;
            }
            switch ((int)type)
            {
                case TransactionNone:
                case TransactionAutomatic:
                    return 0;
                case TransactionSingle:
                    single = true;
                    return 0;
                default:
                    return (int)MessageQueueErrorCode.TransactionUsage;
            }
        }

        // Sending.

        public static int SendMessage(MessageQueueHandle handle, MessagePropertyVariants.MQPROPS properties, IntPtr transactionType, ITransaction transaction)
        {
            if (!queues.TryGetValue(handle.DangerousGetHandle(), out QueueHandle queue))
                return (int)MessageQueueErrorCode.InvalidHandle;
            int status = Transaction(transactionType, transaction, out StoreTransaction internalTransaction, out bool single);
            if (status != 0)
                return status;

            PropertyBlock block = new PropertyBlock(properties);
            MessageRecord record = new MessageRecord();
            status = MessageProperties.ReadSend(block, record);
            if (status != 0)
                return status;

            bool transactional = internalTransaction != null || single;
            using (SqliteConnection connection = StoreDatabase.Open(out Guid machineId))
            using (SqliteTransaction write = connection.BeginTransaction(deferred: false))
            {
                QueueRow row = QueueRow.Find(connection, new QueueNames.QueueReference { Id = queue.Id });
                if (row == null)
                    return (int)MessageQueueErrorCode.QueueDeleted;
                // A local queue takes transactional messages if it's transactional, and only then.
                if (row.Transactional != transactional)
                    return (int)MessageQueueErrorCode.TransactionUsage;

                if (transactional)
                {
                    // Messages in a transactional queue keep the order they were sent in.
                    record.Priority = 0;
                    record.TransactionId = internalTransaction != null
                        ? internalTransaction.TransactionId
                        : MessageProperties.MakeId(machineId, NextTransactionNumber(connection));
                    record.FirstInTransaction = single || !internalTransaction.Sent;
                    record.LastInTransaction = single;
                }

                record.SentTime = record.ArrivedTime = Now();
                using (SqliteCommand command = connection.CreateCommand())
                {
                    command.CommandText =
                        "INSERT INTO messages (queue_id, message_id, correlation_id, label, body, body_type, priority, " +
                        "app_specific, extension, response_queue, sent, arrived, transaction_id, first_in_transaction, " +
                        "last_in_transaction, properties, state, transaction_owner) VALUES ($queue, zeroblob(20), " +
                        "$correlation, $label, $body, $bodyType, $priority, $appSpecific, $extension, $response, $sent, " +
                        "$arrived, $transactionId, $first, $last, $properties, $state, $owner) RETURNING lookup_id";
                    command.Parameters.AddWithValue("$queue", queue.Id);
                    command.Parameters.AddWithValue("$correlation", (object)record.CorrelationId ?? DBNull.Value);
                    command.Parameters.AddWithValue("$label", (object)record.Label ?? DBNull.Value);
                    command.Parameters.AddWithValue("$body", (object)record.Body ?? DBNull.Value);
                    command.Parameters.AddWithValue("$bodyType", record.BodyType);
                    command.Parameters.AddWithValue("$priority", record.Priority);
                    command.Parameters.AddWithValue("$appSpecific", record.AppSpecific);
                    command.Parameters.AddWithValue("$extension", (object)record.Extension ?? DBNull.Value);
                    command.Parameters.AddWithValue("$response", (object)record.ResponseQueue ?? DBNull.Value);
                    command.Parameters.AddWithValue("$sent", record.SentTime);
                    command.Parameters.AddWithValue("$arrived", record.ArrivedTime);
                    command.Parameters.AddWithValue("$transactionId", (object)record.TransactionId ?? DBNull.Value);
                    command.Parameters.AddWithValue("$first", record.FirstInTransaction ? 1 : 0);
                    command.Parameters.AddWithValue("$last", record.LastInTransaction ? 1 : 0);
                    command.Parameters.AddWithValue("$properties", (object)record.Other ?? DBNull.Value);
                    // Sent in a transaction: hidden until it commits.
                    command.Parameters.AddWithValue("$state", internalTransaction != null ? 1 : 0);
                    command.Parameters.AddWithValue("$owner", internalTransaction != null ? internalTransaction.Id : (object)DBNull.Value);
                    record.LookupId = (long)command.ExecuteScalar();
                }

                record.MessageId = MessageProperties.MakeId(machineId, record.LookupId);
                using (SqliteCommand command = connection.CreateCommand())
                {
                    command.CommandText = "UPDATE messages SET message_id = $id WHERE lookup_id = $lookup";
                    command.Parameters.AddWithValue("$id", record.MessageId);
                    command.Parameters.AddWithValue("$lookup", record.LookupId);
                    command.ExecuteNonQuery();
                }
                write.Commit();
            }

            if (internalTransaction != null)
                internalTransaction.Sent = true;
            // MSMQ gives the caller the new message's ID.
            MessageProperties.WriteMessageId(block, record.MessageId);
            Arrivals.Signal();
            return 0;
        }

        /// <summary>A transaction number for a Single message's transaction ID, from the transactions sequence.</summary>
        private static long NextTransactionNumber(SqliteConnection connection)
        {
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText =
                    "INSERT INTO transactions (host, process_id, started) VALUES ('', 0, 0) RETURNING id";
                long id = (long)command.ExecuteScalar();
                command.CommandText = "DELETE FROM transactions WHERE id = $id";
                command.Parameters.AddWithValue("$id", id);
                command.ExecuteNonQuery();
                return id;
            }
        }

        // Receiving.

        public static int ReceiveMessage(MessageQueueHandle handle, uint timeout, int action, MessagePropertyVariants.MQPROPS properties,
                                         Action<int> completion, CursorHandle cursorHandle, IntPtr transactionType, ITransaction transaction)
        {
            if (!queues.TryGetValue(handle.DangerousGetHandle(), out QueueHandle queue))
                return (int)MessageQueueErrorCode.InvalidHandle;
            QueueCursor cursor = null;
            if (cursorHandle != null && !cursorHandle.IsInvalid)
            {
                if (!cursors.TryGetValue(cursorHandle.DangerousGetHandle(), out cursor) || cursor.Queue.Id != queue.Id)
                    return (int)MessageQueueErrorCode.InvalidHandle;
            }
            if (action != ActionReceive && action != ActionPeekCurrent && action != ActionPeekNext)
                return (int)MessageQueueErrorCode.IllegalCursorAction;
            if (action == ActionPeekNext && cursor == null)
                return (int)MessageQueueErrorCode.IllegalCursorAction;

            StoreTransaction internalTransaction = null;
            bool single = false;
            if (action == ActionReceive)
            {
                int status = Transaction(transactionType, transaction, out internalTransaction, out single);
                if (status != 0)
                    return status;
            }

            PropertyBlock block = new PropertyBlock(properties);
            Receive receive = new Receive
            {
                Queue = queue,
                Cursor = cursor,
                Action = action,
                Block = block,
                Transaction = internalTransaction,
                Transactional = internalTransaction != null || single,
                Timeout = timeout,
            };
            return Run(receive, completion);
        }

        public static int ReceiveMessageByLookupId(MessageQueueHandle handle, long lookupId, int action, MessagePropertyVariants.MQPROPS properties,
                                                   Action<int> completion, IntPtr transactionType, ITransaction transaction)
        {
            if (!queues.TryGetValue(handle.DangerousGetHandle(), out QueueHandle queue))
                return (int)MessageQueueErrorCode.InvalidHandle;
            int mask = action & ~0xF;
            if (mask != LookupPeek && mask != LookupReceive)
                return (int)MessageQueueErrorCode.InvalidParameter;

            StoreTransaction internalTransaction = null;
            bool single = false;
            if (mask == LookupReceive)
            {
                int status = Transaction(transactionType, transaction, out internalTransaction, out single);
                if (status != 0)
                    return status;
            }

            Receive receive = new Receive
            {
                Queue = queue,
                Action = action,
                Block = new PropertyBlock(properties),
                Transaction = internalTransaction,
                Transactional = internalTransaction != null || single,
                ByLookupId = true,
                LookupId = lookupId,
            };
            return Run(receive, completion);
        }

        private sealed class Receive
        {
            internal QueueHandle Queue;
            internal QueueCursor Cursor;
            internal int Action;
            internal PropertyBlock Block;
            internal StoreTransaction Transaction;
            internal bool Transactional;
            internal uint Timeout;
            internal bool ByLookupId;
            internal long LookupId;

            internal bool Removes => ByLookupId ? (Action & ~0xF) == LookupReceive : Action == ActionReceive;
        }

        /// <summary>
        /// A receive, synchronous, or, with a completion callback, on a thread of its own: MSMQ's callback path, which
        /// returns OPERATION_PENDING now and calls back with the result.
        /// </summary>
        private static int Run(Receive receive, Action<int> completion)
        {
            if (completion == null)
                return Wait(receive);

            Task.Factory.StartNew(() =>
            {
                int result;
                try
                {
                    result = Wait(receive);
                }
                catch (MessageQueueException e)
                {
                    result = (int)e.MessageQueueErrorCode;
                }
                catch (Exception)
                {
                    result = (int)MessageQueueErrorCode.Generic;
                }
                completion(result);
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            return OperationPending;
        }

        /// <summary>Tries the receive until it finds a message or its timeout passes.</summary>
        private static int Wait(Receive receive)
        {
            long deadline = receive.Timeout == InfiniteTimeout ? long.MaxValue : Environment.TickCount64 + receive.Timeout;
            while (true)
            {
                if (receive.Queue.Closed || (receive.Cursor != null && receive.Cursor.Closed))
                    return (int)MessageQueueErrorCode.OperationCanceled;

                long seen = Arrivals.Version;
                bool found = true;
                int status = receive.ByLookupId ? TryByLookupId(receive) : TryReceive(receive, out found);
                if (found)
                    return status;

                long remaining = deadline - Environment.TickCount64;
                if (remaining <= 0)
                    return (int)MessageQueueErrorCode.IOTimeout;
                Arrivals.Wait(seen, (int)Math.Min(remaining, int.MaxValue));
            }
        }

        private static int TryReceive(Receive receive, out bool found)
        {
            found = true;
            QueueCursor cursor = receive.Cursor;
            using (SqliteConnection connection = StoreDatabase.Open(out Guid machineId))
            using (SqliteTransaction read = connection.BeginTransaction(deferred: !receive.Removes))
            {
                QueueRow queue = QueueRow.Find(connection, new QueueNames.QueueReference { Id = receive.Queue.Id });
                if (queue == null)
                    return (int)MessageQueueErrorCode.QueueDeleted;
                if (receive.Removes && receive.Transactional && !queue.Transactional)
                    return (int)MessageQueueErrorCode.TransactionUsage;

                MessageRecord message;
                if (cursor == null)
                    message = Find(connection, read, queue.Id, "", null);
                else if (receive.Action == ActionPeekNext)
                {
                    if (!cursor.Positioned)
                        return (int)MessageQueueErrorCode.IllegalCursorAction;
                    if (cursor.Advance)
                    {
                        // The cursor is at the message after the one received through it: the next is after that.
                        MessageRecord at = After(connection, read, queue.Id, cursor.Priority, cursor.LookupId);
                        if (at == null)
                        {
                            // At the end of the queue: the next message is the next to arrive.
                            cursor.Advance = false;
                            message = null;
                        }
                        else
                            message = After(connection, read, queue.Id, at.Priority, at.LookupId);
                    }
                    else
                        message = After(connection, read, queue.Id, cursor.Priority, cursor.LookupId);
                }
                else if (!cursor.Positioned)
                    message = Find(connection, read, queue.Id, "", null);
                else if (cursor.Advance)
                    message = After(connection, read, queue.Id, cursor.Priority, cursor.LookupId);
                else
                {
                    // The message the cursor is at, or, if it has been received since, the one after it.
                    message = Find(connection, read, queue.Id, " AND lookup_id = $lookup", c => c.Parameters.AddWithValue("$lookup", cursor.LookupId))
                              ?? After(connection, read, queue.Id, cursor.Priority, cursor.LookupId);
                }

                if (message == null)
                {
                    found = false;
                    return 0;
                }

                int status = MessageProperties.WriteReceive(receive.Block, message, receive.Queue.FormatName, machineId);
                bool removed = receive.Removes && status == 0;
                if (removed)
                {
                    Remove(connection, read, message, receive.Transaction);
                    read.Commit();
                }

                if (cursor != null)
                {
                    // PEEK_NEXT moves even when the buffers were too small (Microsoft's code retries with PEEK_CURRENT);
                    // a receive moves the cursor to the next message.
                    cursor.Positioned = true;
                    cursor.Advance = removed;
                    cursor.Priority = message.Priority;
                    cursor.LookupId = message.LookupId;
                }
                return status;
            }
        }

        private static int TryByLookupId(Receive receive)
        {
            using (SqliteConnection connection = StoreDatabase.Open(out Guid machineId))
            using (SqliteTransaction read = connection.BeginTransaction(deferred: !receive.Removes))
            {
                QueueRow queue = QueueRow.Find(connection, new QueueNames.QueueReference { Id = receive.Queue.Id });
                if (queue == null)
                    return (int)MessageQueueErrorCode.QueueDeleted;
                if (receive.Removes && receive.Transactional && !queue.Transactional)
                    return (int)MessageQueueErrorCode.TransactionUsage;

                // Lookup IDs order the messages by arrival.
                long id = receive.LookupId;
                Action<SqliteCommand> lookup = c => c.Parameters.AddWithValue("$lookup", id);
                MessageRecord message;
                switch ((MessageLookupAction)(receive.Action & 0xF))
                {
                    case MessageLookupAction.Current:
                        message = Find(connection, read, queue.Id, " AND lookup_id = $lookup", lookup);
                        break;
                    case MessageLookupAction.Next:
                        message = Find(connection, read, queue.Id, " AND lookup_id > $lookup ORDER BY lookup_id LIMIT 1", lookup, ordered: true);
                        break;
                    case MessageLookupAction.Previous:
                        message = Find(connection, read, queue.Id, " AND lookup_id < $lookup ORDER BY lookup_id DESC LIMIT 1", lookup, ordered: true);
                        break;
                    case MessageLookupAction.First:
                        message = Find(connection, read, queue.Id, " ORDER BY lookup_id LIMIT 1", null, ordered: true);
                        break;
                    case MessageLookupAction.Last:
                        message = Find(connection, read, queue.Id, " ORDER BY lookup_id DESC LIMIT 1", null, ordered: true);
                        break;
                    default:
                        return (int)MessageQueueErrorCode.InvalidParameter;
                }
                if (message == null)
                    return (int)MessageQueueErrorCode.MessageNotFound;

                int status = MessageProperties.WriteReceive(receive.Block, message, receive.Queue.FormatName, machineId);
                if (receive.Removes && status == 0)
                {
                    Remove(connection, read, message, receive.Transaction);
                    read.Commit();
                }
                return status;
            }
        }

        /// <summary>A received message: deleted, or, in a transaction, hidden until it ends.</summary>
        private static void Remove(SqliteConnection connection, SqliteTransaction transaction, MessageRecord message, StoreTransaction owner)
        {
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.Parameters.AddWithValue("$lookup", message.LookupId);
                if (owner == null)
                    command.CommandText = "DELETE FROM messages WHERE lookup_id = $lookup";
                else
                {
                    command.CommandText = "UPDATE messages SET state = 2, transaction_owner = $owner WHERE lookup_id = $lookup";
                    command.Parameters.AddWithValue("$owner", owner.Id);
                }
                command.ExecuteNonQuery();
            }
        }

        /// <summary>The first message after (priority, lookup ID) in MSMQ's order.</summary>
        private static MessageRecord After(SqliteConnection connection, SqliteTransaction transaction, long queue, long priority, long lookupId)
        {
            return Find(connection, transaction, queue,
                        " AND (priority < $priority OR (priority = $priority AND lookup_id > $lookup))",
                        c =>
                        {
                            c.Parameters.AddWithValue("$priority", priority);
                            c.Parameters.AddWithValue("$lookup", lookupId);
                        });
        }

        /// <summary>The first visible message of a queue that matches a condition, in MSMQ's order unless given.</summary>
        private static MessageRecord Find(SqliteConnection connection, SqliteTransaction transaction, long queue, string condition,
                                          Action<SqliteCommand> parameters, bool ordered = false)
        {
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "SELECT " + MessageColumns + " FROM messages WHERE queue_id = $queue AND state = 0" +
                                      condition + (ordered ? "" : QueueOrder);
                command.Parameters.AddWithValue("$queue", queue);
                parameters?.Invoke(command);
                using (SqliteDataReader reader = command.ExecuteReader())
                {
                    if (!reader.Read())
                        return null;
                    return new MessageRecord
                    {
                        LookupId = reader.GetInt64(0),
                        MessageId = Blob(reader, 1),
                        CorrelationId = Blob(reader, 2),
                        Label = reader.IsDBNull(3) ? null : reader.GetString(3),
                        Body = Blob(reader, 4),
                        BodyType = reader.GetInt32(5),
                        Priority = reader.GetInt32(6),
                        AppSpecific = reader.GetInt32(7),
                        Extension = Blob(reader, 8),
                        ResponseQueue = reader.IsDBNull(9) ? null : reader.GetString(9),
                        SentTime = reader.GetInt64(10),
                        ArrivedTime = reader.GetInt64(11),
                        TransactionId = Blob(reader, 12),
                        FirstInTransaction = reader.GetInt32(13) != 0,
                        LastInTransaction = reader.GetInt32(14) != 0,
                        Other = Blob(reader, 15),
                    };
                }
            }
        }

        internal static byte[] Blob(SqliteDataReader reader, int column)
        {
            return reader.IsDBNull(column) ? null : (byte[])reader.GetValue(column);
        }

        // Cursors.

        public static int CreateCursor(MessageQueueHandle handle, out CursorHandle cursorHandle)
        {
            cursorHandle = CursorHandle.NullHandle;
            if (!queues.TryGetValue(handle.DangerousGetHandle(), out QueueHandle queue))
                return (int)MessageQueueErrorCode.InvalidHandle;
            IntPtr value = NewHandle();
            cursors[value] = new QueueCursor { Queue = queue };
            cursorHandle = CursorHandle.FromStore(value);
            return 0;
        }

        public static int CloseCursor(IntPtr handle)
        {
            if (cursors.TryRemove(handle, out QueueCursor cursor))
            {
                cursor.Closed = true;
                Arrivals.Signal();
            }
            return 0;
        }
    }
}
