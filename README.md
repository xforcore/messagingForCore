# messagingForCore

**System.Messaging on .NET 10, on Linux, Windows and macOS: Microsoft's System.Messaging, with local private queues
stored in a SQLite database instead of MSMQ.**

System.Messaging, .NET Framework's API for Microsoft Message Queuing (MSMQ), was never ported to .NET Core, and MSMQ
itself is Windows-only. A .NET Framework application that sends or receives through `MessageQueue` therefore has
nothing to call when it moves to .NET, and nothing at all on Linux.

messagingForCore keeps Microsoft's System.Messaging source from the .NET Framework 4.8 Reference Source (the public
API, argument checks, exceptions, `Message`'s properties, the formatters) and replaces the calls into MSMQ's native
runtime (`mqrt.dll`) with managed code that keeps queues and messages in a SQLite database. The assembly keeps
Microsoft's identity, `System.Messaging, Version=4.0.0.0, PublicKeyToken=b03f5f7f11d50a3a`, so code and libraries
compiled against System.Messaging bind to it unchanged.

The scope is deliberately limited to what applications on one machine need: local private queues, sending, receiving
and peeking, cursors, asynchronous receive, internal transactions, and the XML and binary formatters. The design is
in [issue #1](https://github.com/xforcore/messagingForCore/issues/1). It is part of the same project as
[webForCore](https://github.com/xforcore/webForCore), which runs ASP.NET 4.x applications on .NET 10, and
[drawingForCore](https://github.com/xforcore/drawingForCore).

## Status

| | |
| --- | --- |
| Status | **Pre-release (0.1.0, unreleased).** The SQLite queue store is in: local private queues, send, receive and peek, cursors, asynchronous receive, internal transactions, the XML and binary formatters, and the `messagingforcore-msmq` setup tool |
| Target | .NET 10 LTS (`net10.0`). Linux x64 and Windows x64, built and tested in CI |
| Tests | In CI on Linux and on Windows: messagingForCore's own 68 tests, all pass; Mono's System.Messaging tests, 87, 85 pass, 2 known failures (Active Directory, and a code access security difference). CI fails on any other failure ([docs/TESTING.md](docs/TESTING.md)) |
| Packages | `messagingForCore` (references `messagingForCore.Messaging`, System.Messaging) and `messagingForCore.Msmq` (the setup tool), on the feed below once released. `BinaryMessageFormatter` needs `webForCore.Serialization.Formatters` from [webForCore](https://github.com/xforcore/webForCore)'s feed, so each messagingForCore version names the webForCore version it uses (0.1.0: webForCore 0.10.0) |

## Using it

Add the feeds to your `nuget.config` (messagingForCore's, and webForCore's for `webForCore.Serialization.Formatters`):

```xml
<configuration>
  <packageSources>
    <add key="messagingForCore" value="https://xforcore.github.io/messagingForCore/index.json" />
    <add key="webForCore" value="https://xforcore.github.io/webForCore/index.json" />
  </packageSources>
</configuration>
```

Reference the package instead of .NET Framework's System.Messaging, and prepare the queue database once per machine
with the tool:

```xml
<PackageReference Include="messagingForCore" Version="0.1.0" />
```

```bash
dotnet tool install -g messagingForCore.Msmq --add-source https://xforcore.github.io/messagingForCore/index.json
messagingforcore-msmq install /var/lib/messagingforcore/queues.db
export MESSAGINGFORCORE_DB=/var/lib/messagingforcore/queues.db
messagingforcore-msmq create-queue '.\private$\orders' --transactional
```

[docs/MIGRATING.md](docs/MIGRATING.md) has the details: containers (mount the folder), no NFS or SMB, the errors.

## Scope

Supported, and failing with a `MessageQueueException` that names the limitation otherwise:
see [docs/NAMESPACES.md](docs/NAMESPACES.md) for the API and [docs/MSMQ-COMPATIBILITY.md](docs/MSMQ-COMPATIBILITY.md)
for how it compares with MSMQ.

- **Supported:** private queues on the local machine (`.\private$\name`, the machine's name, and the
  `FormatName:DIRECT=OS:` forms); create, delete, exists, purge and queue properties; send, receive and peek with
  timeouts, by ID, correlation ID and lookup ID; cursors and enumerators; `BeginReceive`/`BeginPeek`;
  `MessageQueueTransaction` and `MessageQueueTransactionType.Single`; `XmlMessageFormatter` and `BinaryMessageFormatter`.
- **Not supported:** remote and public queues, Active Directory, HTTP format names, `Automatic` (DTC) transactions,
  queue security, COM objects in `ActiveXMessageFormatter` bodies, acknowledgements, journal and dead-letter queues,
  message time limits (stored, not acted on), and
  databases on network file systems.

**Not MSMQ, even on Windows.** messagingForCore never calls MSMQ, so a migrated application can't exchange messages
with applications still running on .NET Framework and MSMQ. Migrate the applications that share a queue together.

## How it's made

Microsoft's code comes from submodules in `external/`: forks under [xforcore](https://github.com/xforcore), cleared
down to the files messagingForCore uses at a fixed commit, with each of messagingForCore's changes as a commit on the
`messagingforcore` branch.

| Project | Assembly | From |
| --- | --- | --- |
| `src/messagingForCore.System.Messaging` | System.Messaging 4.0.0.0 | [microsoft/referencesource](https://github.com/microsoft/referencesource) (.NET Framework 4.8), `System.Messaging/` |
| `src/messagingForCore.System.Messaging/Store` | (in System.Messaging) | messagingForCore's own: the queue store on SQLite (Microsoft.Data.Sqlite), in place of `mqrt.dll` |
| `src/messagingForCore.Msmq` | `messagingforcore-msmq` (a .NET tool) | messagingForCore's own: the database's schema, and queue administration |
| `src/messagingForCore` | none: the package that references the others | |
| `test/messagingForCore.System.Messaging.MonoTests` | Mono's System.Messaging tests | [mono/mono](https://github.com/mono/mono), `mcs/class/System.Messaging/Test` |
| `test/messagingForCore.System.Messaging.Tests` | messagingForCore's own tests | |

- [docs/MODIFICATIONS.md](docs/MODIFICATIONS.md): every change from Microsoft's source and Mono's tests, and why.
- [docs/LIFT.md](docs/LIFT.md): where Microsoft's code and Mono's tests come from, and how to change them.
- [docs/NAMESPACES.md](docs/NAMESPACES.md) and [docs/MSMQ-COMPATIBILITY.md](docs/MSMQ-COMPATIBILITY.md): the scope, and how it compares with MSMQ.
- [docs/TESTING.md](docs/TESTING.md): running the tests, and the known failures.
- [docs/MIGRATING.md](docs/MIGRATING.md): moving an application that uses System.Messaging.
- [CONTRIBUTING.md](CONTRIBUTING.md): how to make a change.

## Building

The .NET SDK in `global.json` (10.0.100, or a later 10.0 feature band), and Bash (Git Bash on Windows) for the
scripts. Clone with `git clone --recurse-submodules`.

```bash
dotnet build messagingForCore.slnx -c Release
tools/ci/run-tests.sh                              # the tests, checked against the known failures
tools/ci/pack.sh 0.1.0-local packages              # the NuGet packages
tools/ci/package-smoke.sh packages 0.1.0-local     # an application built from the packages
```

## Repository layout

```text
external/                             # Microsoft's and Mono's code: submodules of the xforcore forks' messagingforcore branches
src/                                  # the projects
test/                                 # Mono's tests and messagingForCore's own
keys/                                 # Microsoft's public key, for public signing
tools/                                # generators, and tests, known failures, packing and the package smoke test (ci/)
docs/                                 # scope, migrating, modifications, testing, licensing, release notes
```

## Licence

MIT. Microsoft's Reference Source is MIT-licensed, Mono's tests are MIT, and messagingForCore's own code is MIT. See
[LICENSE](LICENSE), [NOTICE](NOTICE) and [docs/LICENSING.md](docs/LICENSING.md). messagingForCore isn't affiliated with
or endorsed by Microsoft. Its assembly carries Microsoft's public key so that references resolve, but it is not signed
by Microsoft.
