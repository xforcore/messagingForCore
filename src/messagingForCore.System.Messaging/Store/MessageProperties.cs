// Licensed under the MIT License. Copyright (c) messagingForCore contributors.
//
// A message's properties: what MQSendMessage stores from the caller's properties, and what MQReceiveMessage writes back
// for the properties the caller asks for (MessagePropertyFilter), following MSMQ's buffer-size protocol. For a string
// or byte buffer whose size goes in a companion property (BODY and BODY_SIZE, LABEL and LABEL_LEN, ...), the store
// sets the companion to the size the value needs and, when the caller's buffer is too small, returns the matching
// "buffer too small" code without consuming the message; Microsoft's code then grows its buffers (Message.AdjustMemory)
// and asks again. The properties the issue's scope reads have columns of their own; the others the caller sends
// (acknowledgement, journal, time limits, admin queue, ...) are kept as sent and returned as they were, but not acted
// on.
using System.Collections.Generic;
using System.IO;
using System.Messaging.Interop;

namespace System.Messaging.Store
{
    /// <summary>A message as stored: the messages table's columns.</summary>
    internal sealed class MessageRecord
    {
        internal long LookupId;
        internal byte[] MessageId;
        internal byte[] CorrelationId;
        internal string Label;
        internal byte[] Body;
        internal int BodyType;
        internal int Priority;
        internal int AppSpecific;
        internal byte[] Extension;
        internal string ResponseQueue;
        internal long SentTime;
        internal long ArrivedTime;
        internal byte[] TransactionId;
        internal bool FirstInTransaction;
        internal bool LastInTransaction;
        internal byte[] Other;
    }

    internal static class MessageProperties
    {
        // PROPID_M_*, from Interop/NativeMethods.cs.
        internal const int CLASS = NativeMethods.MESSAGE_PROPID_CLASS;
        internal const int MSGID = NativeMethods.MESSAGE_PROPID_MSGID;
        internal const int CORRELATIONID = NativeMethods.MESSAGE_PROPID_CORRELATIONID;
        internal const int PRIORITY = NativeMethods.MESSAGE_PROPID_PRIORITY;
        internal const int BODY = NativeMethods.MESSAGE_PROPID_BODY;
        internal const int BODY_SIZE = NativeMethods.MESSAGE_PROPID_BODY_SIZE;
        internal const int BODY_TYPE = NativeMethods.MESSAGE_PROPID_BODY_TYPE;
        internal const int LABEL = NativeMethods.MESSAGE_PROPID_LABEL;
        internal const int LABEL_LEN = NativeMethods.MESSAGE_PROPID_LABEL_LEN;
        internal const int APPSPECIFIC = NativeMethods.MESSAGE_PROPID_APPSPECIFIC;
        internal const int EXTENSION = NativeMethods.MESSAGE_PROPID_EXTENSION;
        internal const int EXTENSION_LEN = NativeMethods.MESSAGE_PROPID_EXTENSION_LEN;
        internal const int RESP_QUEUE = NativeMethods.MESSAGE_PROPID_RESP_QUEUE;
        internal const int RESP_QUEUE_LEN = NativeMethods.MESSAGE_PROPID_RESP_QUEUE_LEN;
        internal const int ADMIN_QUEUE = NativeMethods.MESSAGE_PROPID_ADMIN_QUEUE;
        internal const int ADMIN_QUEUE_LEN = NativeMethods.MESSAGE_PROPID_ADMIN_QUEUE_LEN;
        internal const int DEST_QUEUE = NativeMethods.MESSAGE_PROPID_DEST_QUEUE;
        internal const int DEST_QUEUE_LEN = NativeMethods.MESSAGE_PROPID_DEST_QUEUE_LEN;
        internal const int XACT_STATUS_QUEUE = NativeMethods.MESSAGE_PROPID_XACT_STATUS_QUEUE;
        internal const int XACT_STATUS_QUEUE_LEN = NativeMethods.MESSAGE_PROPID_XACT_STATUS_QUEUE_LEN;
        internal const int PROV_NAME = NativeMethods.MESSAGE_PROPID_PROV_NAME;
        internal const int PROV_NAME_LEN = NativeMethods.MESSAGE_PROPID_PROV_NAME_LEN;
        internal const int SENDERID = NativeMethods.MESSAGE_PROPID_SENDERID;
        internal const int SENDERID_LEN = NativeMethods.MESSAGE_PROPID_SENDERID_LEN;
        internal const int SENDER_CERT = NativeMethods.MESSAGE_PROPID_SENDER_CERT;
        internal const int SENDER_CERT_LEN = NativeMethods.MESSAGE_PROPID_SENDER_CERT_LEN;
        internal const int SIGNATURE = NativeMethods.MESSAGE_PROPID_SIGNATURE;
        internal const int SIGNATURE_LEN = NativeMethods.MESSAGE_PROPID_SIGNATURE_LEN;
        internal const int DEST_SYMM_KEY = NativeMethods.MESSAGE_PROPID_DEST_SYMM_KEY;
        internal const int DEST_SYMM_KEY_LEN = NativeMethods.MESSAGE_PROPID_DEST_SYMM_KEY_LEN;
        internal const int SENTTIME = NativeMethods.MESSAGE_PROPID_SENTTIME;
        internal const int ARRIVEDTIME = NativeMethods.MESSAGE_PROPID_ARRIVEDTIME;
        internal const int SRC_MACHINE_ID = NativeMethods.MESSAGE_PROPID_SRC_MACHINE_ID;
        internal const int XACTID = NativeMethods.MESSAGE_PROPID_XACTID;
        internal const int FIRST_IN_XACT = NativeMethods.MESSAGE_PROPID_FIRST_IN_XACT;
        internal const int LAST_IN_XACT = NativeMethods.MESSAGE_PROPID_LAST_IN_XACT;
        internal const int LOOKUPID = NativeMethods.MESSAGE_PROPID_LOOKUPID;
        internal const int AUTHENTICATED = NativeMethods.MESSAGE_PROPID_AUTHENTICATED;
        internal const int SECURITY_CONTEXT = NativeMethods.MESSAGE_PROPID_SECURITY_CONTEXT;
        internal const int TIME_TO_BE_RECEIVED = NativeMethods.MESSAGE_PROPID_TIME_TO_BE_RECEIVED;
        internal const int TIME_TO_REACH_QUEUE = NativeMethods.MESSAGE_PROPID_TIME_TO_REACH_QUEUE;

        internal const int IdSize = 20;

        // MSMQ's limit on a message (4 MB).
        internal const int MaxMessageSize = 4 * 1024 * 1024;

        // MQ_PRIORITY default (MQ_DEFAULT_PRIORITY).
        internal const int DefaultPriority = 3;

        // INFINITE, MSMQ's default for both time limits.
        private const long Infinite = 0xFFFFFFFFL;

        /// <summary>A byte buffer, its size property and the code for a buffer that is too small.</summary>
        private static readonly Dictionary<int, (int Size, int TooSmall)> byteBuffers = new Dictionary<int, (int, int)>
        {
            { BODY, (BODY_SIZE, (int)MessageQueueErrorCode.BufferOverflow) },
            { EXTENSION, (EXTENSION_LEN, (int)MessageQueueErrorCode.BufferOverflow) },
            { SENDERID, (SENDERID_LEN, (int)MessageQueueErrorCode.SenderIdBufferTooSmall) },
            { SENDER_CERT, (SENDER_CERT_LEN, (int)MessageQueueErrorCode.SenderCertificateBufferTooSmall) },
            { SIGNATURE, (SIGNATURE_LEN, (int)MessageQueueErrorCode.SignatureBufferTooSmall) },
            { DEST_SYMM_KEY, (DEST_SYMM_KEY_LEN, (int)MessageQueueErrorCode.SymmetricKeyBufferTooSmall) },
        };

        /// <summary>A string buffer, its length property (in characters) and the code for a buffer that is too small.</summary>
        private static readonly Dictionary<int, (int Length, int TooSmall)> stringBuffers = new Dictionary<int, (int, int)>
        {
            { LABEL, (LABEL_LEN, (int)MessageQueueErrorCode.LabelBufferTooSmall) },
            { RESP_QUEUE, (RESP_QUEUE_LEN, (int)MessageQueueErrorCode.FormatNameBufferTooSmall) },
            { ADMIN_QUEUE, (ADMIN_QUEUE_LEN, (int)MessageQueueErrorCode.FormatNameBufferTooSmall) },
            { DEST_QUEUE, (DEST_QUEUE_LEN, (int)MessageQueueErrorCode.FormatNameBufferTooSmall) },
            { XACT_STATUS_QUEUE, (XACT_STATUS_QUEUE_LEN, (int)MessageQueueErrorCode.FormatNameBufferTooSmall) },
            { PROV_NAME, (PROV_NAME_LEN, (int)MessageQueueErrorCode.ProviderNameBufferTooSmall) },
        };

        private static bool IsSizeProperty(int id)
        {
            foreach (var entry in byteBuffers.Values)
                if (entry.Size == id)
                    return true;
            foreach (var entry in stringBuffers.Values)
                if (entry.Length == id)
                    return true;
            return false;
        }

        /// <summary>
        /// The record for a send, from the caller's properties; a status for properties MSMQ would refuse.
        /// </summary>
        internal static int ReadSend(PropertyBlock block, MessageRecord record)
        {
            var other = new MemoryStream();
            var writer = new BinaryWriter(other);
            record.Priority = DefaultPriority;

            for (int i = 0; i < block.Count; i++)
            {
                int id = block.Id(i);
                switch (id)
                {
                    case MSGID:
                    case LOOKUPID:
                    case SECURITY_CONTEXT:
                        // Set by the store (MSGID is written back), or not stored.
                        break;
                    case CORRELATIONID:
                        record.CorrelationId = block.Bytes(i, IdSize);
                        break;
                    case PRIORITY:
                        record.Priority = (int)block.Integer(i);
                        if (record.Priority < 0 || record.Priority > 7)
                            return (int)MessageQueueErrorCode.IllegalPropertyValue;
                        break;
                    case BODY:
                        record.Body = block.Bytes(i, block.Integer(BODY_SIZE, long.MaxValue));
                        if (record.Body.Length > MaxMessageSize)
                            return (int)MessageQueueErrorCode.InsufficientResources;
                        break;
                    case BODY_TYPE:
                        record.BodyType = (int)block.Integer(i);
                        break;
                    case LABEL:
                        record.Label = block.String(i, block.Integer(LABEL_LEN, -1));
                        break;
                    case APPSPECIFIC:
                        record.AppSpecific = (int)block.Integer(i);
                        break;
                    case EXTENSION:
                        record.Extension = block.Bytes(i, block.Integer(EXTENSION_LEN, long.MaxValue));
                        break;
                    case RESP_QUEUE:
                        record.ResponseQueue = block.String(i, block.Integer(RESP_QUEUE_LEN, -1));
                        break;
                    default:
                        if (IsSizeProperty(id))
                            break;
                        WriteOther(writer, block, i, id);
                        break;
                }
            }

            writer.Flush();
            record.Other = other.Length == 0 ? null : other.ToArray();
            return 0;
        }

        /// <summary>The properties kept as sent: (id, vt, length, value) records.</summary>
        private static void WriteOther(BinaryWriter writer, PropertyBlock block, int index, int id)
        {
            short vt = block.Vt(index);
            byte[] value;
            if (vt == PropertyBlock.VT_VECTOR_UI1)
            {
                int length;
                value = ByteBufferLength(block, id, out length) ? block.Bytes(index, length) : block.Bytes(index);
            }
            else if (vt == PropertyBlock.VT_LPWSTR)
            {
                int lengthProperty = stringBuffers.TryGetValue(id, out var s) ? s.Length : -1;
                string text = block.String(index, lengthProperty < 0 ? -1 : block.Integer(lengthProperty, -1)) ?? "";
                value = System.Text.Encoding.Unicode.GetBytes(text);
            }
            else if (vt == PropertyBlock.VT_CLSID)
                value = block.Guid(index).ToByteArray();
            else if (vt == PropertyBlock.VT_EMPTY || vt == PropertyBlock.VT_NULL)
                return;
            else
                value = BitConverter.GetBytes(block.Integer(index));

            writer.Write(id);
            writer.Write(vt);
            writer.Write(value.Length);
            writer.Write(value);
        }

        private static bool ByteBufferLength(PropertyBlock block, int id, out int length)
        {
            length = 0;
            if (!byteBuffers.TryGetValue(id, out var entry))
                return false;
            long value = block.Integer(entry.Size, -1);
            if (value < 0)
                return false;
            length = (int)value;
            return true;
        }

        private static Dictionary<int, (short Vt, byte[] Value)> ReadOther(byte[] other)
        {
            var result = new Dictionary<int, (short, byte[])>();
            if (other == null)
                return result;
            var reader = new BinaryReader(new MemoryStream(other));
            while (reader.BaseStream.Position < reader.BaseStream.Length)
            {
                int id = reader.ReadInt32();
                short vt = reader.ReadInt16();
                int length = reader.ReadInt32();
                result[id] = (vt, reader.ReadBytes(length));
            }
            return result;
        }

        /// <summary>
        /// Writes a message into the caller's properties. Returns 0, or the first "buffer too small" code; the sizes
        /// are set either way, and nothing else is written for a buffer that is too small.
        /// </summary>
        internal static int WriteReceive(PropertyBlock block, MessageRecord record, string queueFormatName, Guid machineId)
        {
            int status = 0;
            Dictionary<int, (short Vt, byte[] Value)> other = ReadOther(record.Other);
            var sizes = new Dictionary<int, long>();

            // The buffers first: their sizes go in companion properties, written after.
            for (int i = 0; i < block.Count; i++)
            {
                int id = block.Id(i);
                if (byteBuffers.TryGetValue(id, out var bytes))
                {
                    byte[] value = id switch
                    {
                        BODY => record.Body,
                        EXTENSION => record.Extension,
                        _ => other.TryGetValue(id, out var o) ? o.Value : null,
                    } ?? Array.Empty<byte>();
                    sizes[bytes.Size] = value.Length;
                    if (value.Length > block.Capacity(i))
                        status = status != 0 ? status : bytes.TooSmall;
                    else
                        block.WriteBytes(i, value);
                }
                else if (stringBuffers.TryGetValue(id, out var strings))
                {
                    string value = id switch
                    {
                        LABEL => record.Label,
                        RESP_QUEUE => record.ResponseQueue,
                        DEST_QUEUE => queueFormatName,
                        _ => other.TryGetValue(id, out var o) ? System.Text.Encoding.Unicode.GetString(o.Value) : null,
                    };
                    // The length includes the terminating null; nothing is 0.
                    long length = string.IsNullOrEmpty(value) ? 0 : value.Length + 1;
                    long capacity = block.Integer(strings.Length, 0);
                    sizes[strings.Length] = length;
                    if (length > capacity)
                        status = status != 0 ? status : strings.TooSmall;
                    else if (length > 0)
                        block.WriteString(i, value);
                }
            }

            for (int i = 0; i < block.Count; i++)
            {
                int id = block.Id(i);
                if (byteBuffers.ContainsKey(id) || stringBuffers.ContainsKey(id))
                    continue;
                if (sizes.TryGetValue(id, out long size))
                {
                    block.SetInteger(i, size);
                    continue;
                }

                switch (id)
                {
                    case MSGID:
                        WriteId(block, i, record.MessageId);
                        break;
                    case CORRELATIONID:
                        WriteId(block, i, record.CorrelationId);
                        break;
                    case XACTID:
                        WriteId(block, i, record.TransactionId);
                        break;
                    case LOOKUPID:
                        block.SetInteger(i, record.LookupId);
                        break;
                    case PRIORITY:
                        block.SetInteger(i, record.Priority);
                        break;
                    case CLASS:
                        // MQMSG_CLASS_NORMAL: messagingForCore has no acknowledgements or reports.
                        block.SetInteger(i, 0);
                        break;
                    case BODY_TYPE:
                        block.SetInteger(i, record.BodyType);
                        break;
                    case APPSPECIFIC:
                        block.SetInteger(i, record.AppSpecific);
                        break;
                    case SENTTIME:
                        block.SetInteger(i, record.SentTime);
                        break;
                    case ARRIVEDTIME:
                        block.SetInteger(i, record.ArrivedTime);
                        break;
                    case FIRST_IN_XACT:
                        block.SetInteger(i, record.FirstInTransaction ? 1 : 0);
                        break;
                    case LAST_IN_XACT:
                        block.SetInteger(i, record.LastInTransaction ? 1 : 0);
                        break;
                    case SRC_MACHINE_ID:
                        block.WriteGuid(i, machineId);
                        break;
                    case AUTHENTICATED:
                        block.SetInteger(i, 0);
                        break;
                    default:
                        if (other.TryGetValue(id, out var stored))
                        {
                            if (stored.Vt == PropertyBlock.VT_CLSID)
                                block.WriteGuid(i, new Guid(stored.Value));
                            else
                                block.SetInteger(i, BitConverter.ToInt64(stored.Value, 0));
                        }
                        else if (id == TIME_TO_BE_RECEIVED || id == TIME_TO_REACH_QUEUE)
                            block.SetInteger(i, Infinite);
                        else if (block.Vt(i) == PropertyBlock.VT_CLSID)
                            block.WriteGuid(i, Guid.Empty);
                        else
                            block.SetInteger(i, 0);
                        break;
                }
            }

            return status;
        }

        /// <summary>The ID of a message just sent, written into the caller's MSGID buffer, as MSMQ did.</summary>
        internal static void WriteMessageId(PropertyBlock block, byte[] id)
        {
            int index = block.Find(MSGID);
            if (index >= 0)
                WriteId(block, index, id);
        }

        private static void WriteId(PropertyBlock block, int index, byte[] id)
        {
            byte[] value = new byte[IdSize];
            if (id != null)
                Array.Copy(id, value, Math.Min(id.Length, IdSize));
            if (block.Capacity(index) >= IdSize)
                block.WriteBytes(index, value);
        }

        /// <summary>MSMQ's message and transaction IDs: the machine's GUID and a 32-bit sequence number.</summary>
        internal static byte[] MakeId(Guid machineId, long sequence)
        {
            byte[] id = new byte[IdSize];
            Array.Copy(machineId.ToByteArray(), id, 16);
            Array.Copy(BitConverter.GetBytes(unchecked((uint)sequence)), 0, id, 16, 4);
            return id;
        }
    }
}
