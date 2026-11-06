# messagingForCore and MSMQ

How a System.Messaging application on messagingForCore compares with the same application on .NET Framework with
MSMQ installed. This is the counterpart of webForCore's IIS-COMPATIBILITY.md. **Status: scaffolding;** the rows
describe the design in [issue #1](https://github.com/xforcore/messagingForCore/issues/1).

| | .NET Framework + MSMQ | messagingForCore |
| --- | --- | --- |
| Platforms | Windows | Linux, Windows, macOS |
| Where queues live | The MSMQ service's store (`%windir%\System32\msmq\storage`) | One SQLite database per machine or shared volume, prepared by a setup tool (`messagingforcore-msmq install`) and named by `MESSAGINGFORCORE_DB` |
| Setting up | Turn on the Windows feature; create queues in Computer Management or in code | `messagingforcore-msmq install` and `create-queue`, or create queues in code |
| Not set up | `InvalidOperationException`: "Message Queuing has not been installed on this computer." | The same, and the message names the setting and the tool. The library never creates the database itself |
| Several processes | Share the service's queues | Share the database file; senders wake receivers in the same process at once, and receivers in other processes poll |
| Containers | MSMQ in Windows containers | Mount the database's folder (not the file: WAL mode keeps `-wal` and `-shm` beside it) |
| Talking to other machines | Remote queues, public queues, HTTP | Not supported |
| Talking to MSMQ | — | Not supported, on Windows either: migrate the applications that share a queue together |
| Transactions | Internal (`MessageQueueTransaction`), and DTC (`Automatic`) | Internal only |
| Security | Queue ACLs, authentication, encryption | None on queues: protect the database file |
| `BinaryMessageFormatter` | .NET Framework's BinaryFormatter | webForCore's port of BinaryFormatter (planned), compatible with .NET Framework's bodies |
