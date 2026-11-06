# Migrating an application that uses System.Messaging

**Status: scaffolding.** messagingForCore builds and its assembly binds, but there's no queue store yet, so an
application gets "Message Queuing has not been installed on this computer" from every queue call. This page is the
plan; each step is filled in as the store lands ([issue #1](https://github.com/xforcore/messagingForCore/issues/1)).

## 1. Check the scope

Compare what the application does with [NAMESPACES.md](NAMESPACES.md). The usual problems:

- **`MessageQueueTransactionType.Automatic`** or a `TransactionScope` around `Receive` (the PetShop pattern): not
  supported. Use a `MessageQueueTransaction`.
- **Remote queues** (`FormatName:DIRECT=TCP:...`, another machine's name): not supported.
- **`SetPermissions` when creating a queue:** fails with a `MessageQueueException`.
- **Queues shared with applications still on .NET Framework and MSMQ:** messagingForCore never calls MSMQ, so those
  applications can't see its queues. Migrate them together.

## 2. Reference the package

Replace the `System.Messaging` framework reference with the package:

```xml
<PackageReference Include="messagingForCore" Version="0.1.0" />
```

Libraries compiled against .NET Framework's System.Messaging bind to it unchanged (the same name, version and public
key token).

## 3. Prepare the database (planned)

```bash
messagingforcore-msmq install /var/lib/messagingforcore/queues.db
messagingforcore-msmq create-queue '.\private$\orders' --transactional --db /var/lib/messagingforcore/queues.db
export MESSAGINGFORCORE_DB=/var/lib/messagingforcore/queues.db
```

- One database per machine, shared by every application that shares queues, as MSMQ was.
- In containers, mount the database's folder, not the file.
- Not on NFS or SMB.

## 4. BinaryMessageFormatter (planned)

.NET removed BinaryFormatter. messagingForCore's `BinaryMessageFormatter` will use webForCore's port
(`webForCore.Serialization.Formatters`), so its version follows webForCore's.
