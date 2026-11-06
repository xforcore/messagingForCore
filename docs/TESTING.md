# Tests

Microsoft never published tests for System.Messaging, so messagingForCore is tested with **Mono's** tests (the ones
Mono wrote for its own System.Messaging, from the `external/mono` submodule, [MODIFICATIONS.md](MODIFICATIONS.md)) and
with **its own**, which cover what Mono's don't: the queue store feature by feature, through System.Messaging's public
API. Every suite runs against a queue database of its own, prepared for the run as `messagingforcore-msmq install`
prepares one, in the test output folder. No test uses MSMQ, on Windows either: messagingForCore never calls it.

**Latest run (6 Nov 2026, Linux x64, .NET 10, Release): 155 tests. messagingForCore's own: 68, all pass. Mono's: 87,
85 pass, 2 fail as known.**

## Running the tests

```bash
dotnet build messagingForCore.slnx -c Release
tools/ci/run-tests.sh            # every suite; .trx files and summary.md in ./TestResults
dotnet run tools/ci/check_code_rules.cs   # CONTRIBUTING.md rules 8 and 9 on messagingForCore's own code
```

One suite: `dotnet test test/messagingForCore.<Suite>/messagingForCore.<Suite>.csproj -c Release`.

CI (`.github/workflows/ci.yml`) runs the same on Ubuntu 24.04 and on Windows Server 2025 (under Git Bash) for every
push and pull request, then packs the packages and runs `tools/ci/package-smoke.sh`: the `messagingForCore.Msmq` tool
is installed from the packages and prepares a database and a queue, and an application that references only the
`messagingForCore` package checks System.Messaging's identity (4.0.0.0, public key token b03f5f7f11d50a3a), the error
without a database, and a transaction with both formatters. A suite fails CI on any failure that isn't in its
known-failures list, and CI reports a known failure that passes so it can be removed.

## The suites

| Suite | Tests | Passed | Known failures | What it exercises |
| --- | ---: | ---: | ---: | --- |
| System.Messaging.Tests (messagingForCore's) | 68 | 68 | 0 | The database check (no setting, no file, no schema, another version, never created), queues by path and format name, queue properties, what isn't supported, message properties and the read filter, MSMQ's order, buffers smaller than the values (the retry), 4 MB, lookup IDs, cursors and enumerators, timeouts and waking, concurrent receivers, `BeginReceive`/`BeginPeek` and their events, cancelling on close, transactions across queues, transaction usage, `Automatic`, the three formatters, the setup tool |
| System.Messaging.MonoTests | 87 | 85 | 2 | Queue administration, send and receive with both formatters, peek, asynchronous peek and receive, selection by ID and correlation ID, enumerators, transactions, `MessageQueuePermissionAttribute` |

Written test first: before the store, 4 of messagingForCore's 68 tests passed (the tool's, which need no store), and
24 of Mono's 87.

Mono's tests create private queues named `.\private$\<guid>` (`MQUtil.CreateQueueName`). With
`MONO_TEST_RABBITMQ_SERVER` set they use that server's name instead, which was for Mono's RabbitMQ provider and has no
use here.

### Known failures

`tools/ci/known-failures/System.Messaging.MonoTests.txt`:

- **`AdminTest.GetPublicQueues`:** out of scope. It creates public queues (`.\admin-queue-3`), which need Active
  Directory; it fails with the `MessageQueueException` that names the limitation.
- **`MessageQueuePermissionAttributeTest.Action`:** a .NET difference. The System.Security.Permissions package's
  `CodeAccessSecurityAttribute` doesn't keep the `SecurityAction` it's given (`Action` reads 0, not `Assert`): code
  access security doesn't exist on .NET. The other 17 permission attribute tests pass.

### Mono's tests changed

Three of Mono's files are changed on the fork (MODIFICATIONS.md, "Test changes"): the asynchronous tests built their
messages with `new Message (new BinaryMessageFormatter ())`, which on .NET Framework makes the formatter the body and
fails there too; and `MessageEnumeratorTest`, which Mono ignored without a reason, runs.
