# What's supported

messagingForCore has one namespace, `System.Messaging` (with `System.Messaging.Design`'s type converters), in one
assembly, System.Messaging 4.0.0.0. Every public type of .NET Framework 4.8's System.Messaging is there, apart from
`MessageQueueInstaller`. What each call does depends on the scope below ([issue #1](https://github.com/xforcore/messagingForCore/issues/1)).

## Supported

| Area | What |
| --- | --- |
| Queues | Private queues on the local machine only: path names `.\private$\name`, `<this machine's name>\private$\name` and `localhost\private$\name`; format names `FormatName:DIRECT=OS:` with any of those, and `PRIVATE=<machine id>\<number>` (what `MessageQueue.FormatName` gives, as MSMQ did). Names are case-insensitive |
| Queue functions | `Create`, `Delete`, `Exists`; opening and closing; path name to format name; `Purge`; `GetPrivateQueuesByMachine` for this machine; `GetMachineId`; queue properties: `Label`, `Transactional`, `UseJournalQueue`, `MaximumQueueSize`, `MaximumJournalSize`, `Category`, `BasePriority`, `Authenticate`, `EncryptionRequired`, `CreateTime`, `LastModifyTime`, `QueueName`, `MachineName`. `Id` is `Guid.Empty`, as for a private queue on MSMQ |
| Message functions | `Send`, `Receive`, `Peek`, each with timeouts; cursors (`Cursor`, `PeekAction.Next`, `GetMessageEnumerator2`, `GetAllMessages`, `RemoveCurrent`); `ReceiveById`/`PeekById`, `ReceiveByCorrelationId`/`PeekByCorrelationId`, `ReceiveByLookupId`/`PeekByLookupId` (all `MessageLookupAction`s); `BeginReceive`/`BeginPeek` and the `ReceiveCompleted`/`PeekCompleted` events |
| Message properties | Body and body type, label, ID, correlation ID, priority (highest first, then in order of arrival), sent and arrived times, app-specific value, extension, response and destination queues, source machine, lookup ID, transaction ID and first/last in transaction; `MessageReadPropertyFilter` chooses which are read, with MSMQ's buffer sizes and retries. Other properties sent (acknowledgement, journal, recoverable, time limits, administration queue, ...) come back as sent but aren't acted on. A message is at most 4 MB, as on MSMQ |
| Transactions | `MessageQueueTransaction` (internal transactions), including several queues in one transaction, and receives and sends together; `MessageQueueTransactionType.Single`; `Automatic` outside a transaction scope (no transaction, as on MSMQ without COM+). MSMQ's rules for local queues: transactional messages to transactional queues only |
| Formatters | `XmlMessageFormatter`, `BinaryMessageFormatter` (on webForCore's BinaryFormatter, .NET Framework's format), and `ActiveXMessageFormatter` for its primitive types |

## Not supported

Each fails with a `MessageQueueException` whose message names the limitation, after MSMQ's message for its code.

| What | Code | Why |
| --- | --- | --- |
| Queues on other computers (`OTHER\private$\q`, `DIRECT=OS:OTHER\...`) | `UnsupportedFormatNameOperation` | A local store on one machine (or one shared volume) |
| Public queues and Active Directory (`.\name`, `PUBLIC=`, `GetPublicQueues*`, `GetMessageQueueEnumerator`, `MQLocate*`) | `UnsupportedOperation` (MSMQ's "workgroup installation" code) | No directory |
| `DIRECT=TCP:`, `DIRECT=HTTP(S):`, `DIRECT=SPX:`, multicast, distribution lists, multiple-element format names | `UnsupportedFormatNameOperation` | No network transport |
| Journal, dead-letter and other system queues (`;JOURNAL`, `MACHINE=`) | `UnsupportedFormatNameOperation` | Not kept |
| `MessageQueueTransactionType.Automatic` inside a `TransactionScope` or COM+ transaction | `TransactionUsage` | No DTC. Enlisting in `System.Transactions` directly may come later |
| Queue security (`ResetPermissions`, security contexts) | `UnsupportedOperation` | No Windows ACLs on the store: protect the database file. `SetPermissions` is refused off Windows by Microsoft's own check (`PlatformNotSupportedException`) before the store |
| `ActiveXMessageFormatter` with a COM object (`IPersistStream`) | `UnsupportedOperation` | Needs Ole32 |
| No database, or one that isn't prepared | `ServiceNotAvailable` | See [MIGRATING.md](MIGRATING.md); the store never creates one |

Not acted on (stored, returned as sent): acknowledgements and administration queues, journal and dead-letter use,
`TimeToReachQueue`, `TimeToBeReceived`, queue quotas. The share mode (`DenyReceive`) isn't enforced. A database on NFS
or SMB isn't supported: SQLite's file locking isn't reliable there. `MessageQueueInstaller` isn't there:
System.Configuration.Install isn't on .NET.
