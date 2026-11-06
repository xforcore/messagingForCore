# Migrating an application that uses System.Messaging

## 1. Check the scope

Compare what the application does with [NAMESPACES.md](NAMESPACES.md). The usual problems:

- **`MessageQueueTransactionType.Automatic` inside a `TransactionScope`** (the PetShop pattern, COM+ components): not
  supported. Use a `MessageQueueTransaction`, or `Single` for one message.
- **Remote queues** (`FormatName:DIRECT=TCP:...`, another machine's name) and **public queues**: not supported.
- **`SetPermissions` when creating a queue:** fails. Protect the database file instead.
- **Queues shared with applications still on .NET Framework and MSMQ:** messagingForCore never calls MSMQ, on Windows
  either, so those applications can't see its queues, and a migrated application can't exchange messages with one that
  hasn't moved. Migrations often go one service at a time; here the applications that share a queue move together.

## 2. Reference the package

messagingForCore's packages are on its feed, and `BinaryMessageFormatter` needs webForCore's
`webForCore.Serialization.Formatters`, from webForCore's feed. Add both to `nuget.config`:

```xml
<configuration>
  <packageSources>
    <add key="messagingForCore" value="https://xforcore.github.io/messagingForCore/index.json" />
    <add key="webForCore" value="https://xforcore.github.io/webForCore/index.json" />
  </packageSources>
</configuration>
```

Then replace the `System.Messaging` framework reference with the package:

```xml
<PackageReference Include="messagingForCore" Version="0.1.0" />
```

Libraries compiled against .NET Framework's System.Messaging bind to it unchanged (the same name, version and public
key token). messagingForCore 0.1.0 uses webForCore.Serialization.Formatters 0.10.0: an application that also uses
webForCore gets the newer of the two versions it references.

## 3. Prepare the database

MSMQ was a Windows feature that someone turned on; messagingForCore's queues live in a SQLite file that the host
prepares once, with the `messagingforcore-msmq` tool (package `messagingForCore.Msmq`):

```bash
dotnet tool install -g messagingForCore.Msmq --add-source https://xforcore.github.io/messagingForCore/index.json
messagingforcore-msmq install /var/lib/messagingforcore/queues.db
export MESSAGINGFORCORE_DB=/var/lib/messagingforcore/queues.db
messagingforcore-msmq create-queue '.\private$\orders' --transactional --label Orders
messagingforcore-msmq list
```

- **One database per machine**, shared by every application that shares queues, as MSMQ was. Each application finds
  it in `MESSAGINGFORCORE_DB`. For one application only (tests), set `MessagingForCore.Database` in the
  runtimeconfig's `configProperties` (or `AppContext.SetData`); it overrides the variable.
- **The library never creates or upgrades the database.** Without the setting, without the file, or with a file that
  isn't a queue database of the right version, every call fails with `MessageQueueException(ServiceNotAvailable)`,
  the error an application got where MSMQ wasn't running, and the message names the setting and the tool. So an
  unmounted volume can't quietly become an empty queue store.
- **Queues:** create them with the tool or in code (`MessageQueue.Create`), as before. `purge` and `delete-queue` are
  there for operations. After an upgrade of messagingForCore that changes the schema, run `messagingforcore-msmq
  upgrade <file>` (the release notes say when).
- **Containers:** mount the database's **folder**, not the file: WAL mode keeps `-wal` and `-shm` files beside it.
  Containers on the same host can share it.
- **Not on NFS or SMB:** SQLite's locking isn't reliable on network file systems. Use a local disk or a local volume.
- **Several processes:** a send wakes receivers in the same process at once; receivers in other processes see it
  within 100 ms (they poll).

## 4. Check the errors

What isn't supported fails with a `MessageQueueException` that names the limitation (see NAMESPACES.md, "Not
supported"), so logs say what to change. Transactional messages go to transactional queues only, as MSMQ required
for local queues: sending without a transaction to a transactional queue fails with `TransactionUsage` rather than
being dropped.
