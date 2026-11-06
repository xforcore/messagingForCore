// Licensed under the MIT License. Copyright (c) messagingForCore contributors.
//
// What messagingForCore doesn't do (issue #1, "Not supported"), as MessageQueueExceptions that name the limitation
// after MSMQ's message for the code. The codes are the ones MSMQ gives in the nearest situation: a workgroup
// installation for Active Directory, an unsupported format name operation, an unsupported operation.
namespace System.Messaging.Store
{
    internal static class Limitations
    {
        private const string Scope = "messagingForCore has local private queues only (.\\private$\\name), stored in SQLite.";

        internal static MessageQueueException RemoteQueue(string name)
        {
            return new MessageQueueException((int)MessageQueueErrorCode.UnsupportedFormatNameOperation,
                "messagingForCore doesn't support queues on other computers (" + name + "). " + Scope);
        }

        internal static MessageQueueException PublicQueue(string name)
        {
            return new MessageQueueException((int)MessageQueueErrorCode.UnsupportedOperation,
                "messagingForCore doesn't support public queues or Active Directory (" + name + "). " + Scope);
        }

        internal static MessageQueueException ActiveDirectory()
        {
            return new MessageQueueException((int)MessageQueueErrorCode.UnsupportedOperation,
                "messagingForCore doesn't support Active Directory lookups (public queues, GetPublicQueues, " +
                "GetMessageQueueEnumerator, MessageQueue.Id). " + Scope);
        }

        internal static MessageQueueException Protocol(string name)
        {
            return new MessageQueueException((int)MessageQueueErrorCode.UnsupportedFormatNameOperation,
                "messagingForCore doesn't support DIRECT=TCP:, DIRECT=HTTP(S): or DIRECT=SPX: format names (" + name +
                "); use DIRECT=OS: or the queue's path. " + Scope);
        }

        internal static MessageQueueException SubQueue(string name)
        {
            return new MessageQueueException((int)MessageQueueErrorCode.UnsupportedFormatNameOperation,
                "messagingForCore doesn't support journal, dead-letter or other system queues (" + name + "). " + Scope);
        }

        internal static MessageQueueException MultipleDestinations(string name)
        {
            return new MessageQueueException((int)MessageQueueErrorCode.UnsupportedFormatNameOperation,
                "messagingForCore doesn't support multicast, distribution lists or multiple-element format names (" +
                name + "). " + Scope);
        }

        internal static MessageQueueException AutomaticTransaction()
        {
            return new MessageQueueException((int)MessageQueueErrorCode.TransactionUsage,
                "messagingForCore doesn't support MessageQueueTransactionType.Automatic inside a System.Transactions " +
                "or COM+ transaction (DTC); use MessageQueueTransaction or MessageQueueTransactionType.Single.");
        }

        internal static MessageQueueException ExternalTransaction()
        {
            return new MessageQueueException((int)MessageQueueErrorCode.TransactionUsage,
                "messagingForCore supports only its own transactions (MessageQueueTransaction, " +
                "MessageQueueTransactionType.Single), not DTC transactions.");
        }

        internal static MessageQueueException Security()
        {
            return new MessageQueueException((int)MessageQueueErrorCode.UnsupportedOperation,
                "messagingForCore doesn't support queue security (SetPermissions, ResetPermissions, security " +
                "contexts); protect the database file instead.");
        }

        internal static MessageQueueException ComObjects()
        {
            return new MessageQueueException((int)MessageQueueErrorCode.UnsupportedOperation,
                "messagingForCore doesn't support COM objects in message bodies (ActiveXMessageFormatter with an " +
                "IPersistStream object); use XmlMessageFormatter or BinaryMessageFormatter.");
        }

        internal static MessageQueueException Management(string objectName)
        {
            return new MessageQueueException((int)MessageQueueErrorCode.UnsupportedOperation,
                "messagingForCore supports only the list of this computer's private queues from MQMgmtGetInfo (" +
                objectName + "). " + Scope);
        }
    }
}
