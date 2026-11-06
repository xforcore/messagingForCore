# Tests

messagingForCore is tested mainly with **other people's tests**. Microsoft never published tests for System.Messaging,
so the suite is Mono's: the tests Mono wrote for its own System.Messaging. They come from the `external/mono`
submodule ([MODIFICATIONS.md](MODIFICATIONS.md)) and run unchanged, apart from what NUnit 3 needs. On top of them,
messagingForCore's own tests will cover what Mono's don't (issue #1): the property buffers and their overflow retry,
format names, the database's schema check, two processes sharing a database, and a `BinaryMessageFormatter` body
written by .NET Framework.

**Latest run (scaffolding, 6 Nov 2026, Linux x64, .NET 10.0.13, Release): 87 tests, 24 pass, 3 are skipped, and 60
fail as known.** 59 of the known failures are calls that need the queue store, which doesn't exist yet: they throw
Microsoft's "Message Queuing has not been installed on this computer", as .NET Framework does where MSMQ isn't
installed. The other is a .NET difference (below).

## Running the tests

```bash
dotnet build messagingForCore.slnx -c Release
tools/ci/run-tests.sh            # every suite; .trx files and summary.md in ./TestResults
```

One suite: `dotnet test test/messagingForCore.<Suite>/messagingForCore.<Suite>.csproj -c Release`.

CI (`.github/workflows/ci.yml`) runs the same script on Ubuntu 24.04 and on Windows Server 2025 (under Git Bash) for
every push and pull request, then packs the packages and runs `tools/ci/package-smoke.sh`: an application that
references only the `messagingForCore` package is built and run, and checks System.Messaging's identity (4.0.0.0,
public key token b03f5f7f11d50a3a) and the error without a queue store. A suite fails CI on any failure that isn't in
its known-failures list, and CI reports a known failure that passes so it can be removed. Both jobs must pass for a
release.

The tests use no MSMQ, on Windows either: messagingForCore never calls it.

## The suites

| Suite | Upstream | Tests | Passed | Skipped | Known failures | What it exercises |
| --- | --- | ---: | ---: | ---: | ---: | --- |
| System.Messaging.MonoTests | Mono `mcs/class/System.Messaging/Test` | 87 | 24 | 3 | 60 | Queue administration, send and receive with both formatters, peek, asynchronous peek and receive, selection by ID and correlation ID, transactions, `MessageQueuePermissionAttribute` |

Mono's tests create private queues named `.\private$\<guid>` (`MQUtil.CreateQueueName`). With
`MONO_TEST_RABBITMQ_SERVER` set they use that server's name instead, which was for Mono's RabbitMQ provider and has no
use here.

### Skipped

- **`MessageEnumeratorTest` (3 tests reported):** Mono ignored the whole fixture, without a reason. Enumerators are in
  scope, so the fixture is to be turned back on once the store has cursors.

### Known failures

`tools/ci/known-failures/System.Messaging.MonoTests.txt`:

- **59 tests: no queue store yet.** Every test that creates, opens, sends to or receives from a queue. Each pull
  request that adds part of the store removes the tests it fixes. `AdminTest.GetPublicQueues` needs Active Directory,
  which is out of scope, and will stay.
- **`MessageQueuePermissionAttributeTest.Action`:** a .NET difference. The System.Security.Permissions package's
  `CodeAccessSecurityAttribute` doesn't keep the `SecurityAction` it's given (`Action` reads 0, not `Assert`): code
  access security doesn't exist on .NET. The other 17 permission attribute tests pass.
