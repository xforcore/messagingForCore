# What's supported

messagingForCore has one namespace, `System.Messaging` (with `System.Messaging.Design`'s type converters), in one
assembly, System.Messaging 4.0.0.0. Every public type of .NET Framework 4.8's System.Messaging is there, apart from
`MessageQueueInstaller`. What each call does depends on the scope below.

**Status: scaffolding.** None of the queue functions work yet: every call that reaches MSMQ throws
`InvalidOperationException` ("Message Queuing has not been installed on this computer"), as .NET Framework does where
MSMQ isn't installed. This page is the scope the SQLite store is being written to ([issue #1](https://github.com/xforcore/messagingForCore/issues/1)).

## Supported

| Area | What |
| --- | --- |
| Queues | Private queues on the local machine only: path names `.\private$\name` and `<this machine's name>\private$\name`; format names `FormatName:DIRECT=OS:.\private$\name` and the machine-name and `localhost` forms |
| Queue functions | `Create`, `Delete`, `Exists`; opening and closing; path name to format name; `Purge`; queue properties (label, transactional, journal flag, maximum size, category) |
| Message functions | `Send`, `Receive`, `Peek`, each with timeouts; cursors (`PeekNext`, `GetMessageEnumerator2`); `ReceiveById`/`PeekById`, `ReceiveByCorrelationId`/`PeekByCorrelationId`, `ReceiveByLookupId`/`PeekByLookupId`; `BeginReceive`/`BeginPeek` and the `ReceiveCompleted`/`PeekCompleted` events |
| Message properties | Body and body type, label, ID, correlation ID, priority, sent and arrived times, app-specific value, extension, response queue; `MessageReadPropertyFilter` chooses which are read |
| Transactions | `MessageQueueTransaction` (internal transactions), including several queues in one transaction; `MessageQueueTransactionType.Single` |
| Formatters | `XmlMessageFormatter`, `BinaryMessageFormatter` |

Formatters and `Message` objects work without a queue, as now.

## Not supported

Each fails with a `MessageQueueException` that names the limitation.

| What | Why |
| --- | --- |
| Remote queues, public queues, Active Directory (`GetPublicQueues*`, `MQLocate*`), HTTP(S) format names | A local store on one machine (or one shared volume) |
| `MessageQueueTransactionType.Automatic` (DTC, COM+) | No DTC. Enlisting in `System.Transactions` directly may come later |
| Queue security (`SetPermissions`, `ResetPermissions`, `AccessControlList`) | No Windows ACLs on the store |
| `ActiveXMessageFormatter` | Needs COM (Ole32) |
| Acknowledgements, administration queues, dead-letter and journal queues, `TimeToReachQueue`, `TimeToBeReceived` | Stored, but not acted on, unless an application needs them |
| A database on NFS or SMB | SQLite's file locking isn't reliable there |
| `MessageQueueInstaller` | System.Configuration.Install isn't on .NET |
