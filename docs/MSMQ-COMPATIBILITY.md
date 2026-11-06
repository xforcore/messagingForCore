# messagingForCore and MSMQ

How a System.Messaging application on messagingForCore compares with the same application on .NET Framework with
MSMQ installed. This is the counterpart of webForCore's IIS-COMPATIBILITY.md.

| | .NET Framework + MSMQ | messagingForCore |
| --- | --- | --- |
| Platforms | Windows | Linux, Windows, macOS |
| Where queues live | The MSMQ service's store (`%windir%\System32\msmq\storage`) | One SQLite database per machine or shared volume, prepared by `messagingforcore-msmq install` and named by `MESSAGINGFORCORE_DB` |
| Setting up | Turn on the Windows feature; create queues in Computer Management or in code | `messagingforcore-msmq install` and `create-queue`, or create queues in code |
| Not set up | `InvalidOperationException` ("Message Queuing has not been installed"), or `MessageQueueException(ServiceNotAvailable)` when the service isn't running | `MessageQueueException(ServiceNotAvailable)`, and the message names the setting and the tool. The library never creates the database |
| Format names | `PRIVATE=`, `DIRECT=OS:`, `DIRECT=TCP:`, `DIRECT=HTTP:`, `PUBLIC=`, multicast, ... | `PRIVATE=` and `DIRECT=OS:` for this machine; the others fail naming the limitation |
| Message IDs | `<machine GUID>\<number>` | The same form; the GUID is the database's |
| Order | Priority, then arrival; transactional messages in order sent | The same |
| Several processes | Share the service's queues | Share the database file; senders wake receivers in the same process at once, and receivers in other processes poll every 100 ms |
| Asynchronous receive | I/O completion ports, or MSMQ's callbacks | MSMQ's callback path, with the receive on a thread of its own |
| Containers | MSMQ in Windows containers | Mount the database's folder (not the file: WAL mode keeps `-wal` and `-shm` beside it) |
| Talking to other machines | Remote queues, public queues, HTTP | Not supported |
| Talking to MSMQ | — | Not supported, on Windows either: migrate the applications that share a queue together |
| Transactions | Internal (`MessageQueueTransaction`), and DTC (`Automatic`) | Internal and `Single`; `Automatic` only outside a transaction scope (no transaction) |
| A process ends in a transaction | MSMQ aborts it | The next process on the same host to open the database aborts it |
| Security | Queue ACLs, authentication, encryption | None on queues: protect the database file |
| Acknowledgements, journals, dead letters, time limits | Acted on | Stored and returned, not acted on |
| `BinaryMessageFormatter` | .NET Framework's BinaryFormatter | webForCore's port of BinaryFormatter: .NET Framework's format, so bodies written by either read on the other |
