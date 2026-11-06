# What messagingForCore changed in Microsoft's code and Mono's tests

messagingForCore runs Microsoft's own System.Messaging rather than a reimplementation of it. Microsoft's source and
Mono's tests come from **submodules** in `external/`: forks of the upstream repositories, cleared down to the files
messagingForCore uses at a pinned commit, built for .NET 10. [LIFT.md](LIFT.md) says how the source was brought in and
how to move it; this page explains the rules for that code and what had to change.

- **Every change is a commit** on the fork's `messagingforcore` branch, with its reason in the message and a
  `// messagingForCore:` comment at the change:

  | Submodule | Fork branch | Upstream commit | Changes |
  | --- | --- | --- | --- |
  | `external/referencesource` | [xforcore/referencesource `messagingforcore`](https://github.com/xforcore/referencesource/commits/messagingforcore) | `ec9fa9a` (microsoft/referencesource, .NET Framework 4.8) | the shared files kept, 9 files changed |
  | `external/mono` | [xforcore/mono `messagingforcore`](https://github.com/xforcore/mono/commits/messagingforcore) | `0f53e9e` (mono/mono) | 3 files changed |

  Each branch starts at the upstream commit, then one commit keeps only the files messagingForCore uses (and the
  licences), then the changes, one file per commit. `git log` on a submodule lists them; `git revert` undoes one.
- Licences and attributions are in [`NOTICE`](../NOTICE) and [`LICENSE`](../LICENSE).

## Rules for Microsoft's code

1. **Don't rewrite.** Each file is upstream's, with its copyright header, at the pinned commit.
2. **Every change is a commit on the fork,** one file per commit, each with a `// messagingForCore:` comment that gives
   the reason (CONTRIBUTING.md principle 4).
3. **messagingForCore's own code stays in the projects:** the queue store (`src/messagingForCore.System.Messaging/Store/`),
   the generated `Res` class and `AssemblyRef`, next to the `Link` items that compile the submodules' files at their
   old paths.
4. **Every file is traceable:** the submodule pins the fork's commit, and the fork's history goes back to upstream's.
5. **Identity is kept.** The assembly keeps Microsoft's name, version and public key (`keys/`, public signed), so
   existing references resolve to it. It isn't signed by Microsoft.
6. **.NET Framework 4.8 is the reference.** Where System.Messaging on .NET would behave differently from .NET
   Framework 4.8 with MSMQ, a change restores 4.8's behaviour.

## Components from submodules

| Component | Upstream (pinned commit) | Licence | Files used | Files changed |
| --- | --- | --- | ---: | ---: |
| System.Messaging | [microsoft/referencesource@ec9fa9a](https://github.com/microsoft/referencesource/tree/ec9fa9ae770d522a5b5f0607898044b7478574a3/System.Messaging) | MIT | 70 of 74 `.cs` files | 9 |
| Shared files (ExternDll, AppContextDefaultValues, LocalAppContext, InvariantComparer) | the same commit: `System.Configuration/misc`, `System.Xml/InternalApis/Clr/inc`, `System/misc` | MIT | 4 | 0 |
| Mono's System.Messaging tests | [mono/mono@0f53e9e](https://github.com/mono/mono/tree/0f53e9e151d92944cacab3e24ac359410c606df6/mcs/class/System.Messaging/Test) | MIT | 10 | 3 |

## Library changes, by kind

| Kind | Change | Where |
| --- | --- | --- |
| **The queue store in place of MSMQ** | Every `[DllImport(ExternDll.Mqrt)]` (MQOpenQueue, MQSendMessage, MQReceiveMessage, MQReceiveMessageByLookupId, MQCreateQueue, MQDeleteQueue, MQGet/SetQueueProperties, MQPathNameToFormatName, MQPurgeQueue, MQCreate/CloseCursor, MQBeginTransaction, MQGetMachineProperties, MQMgmtGetInfo, MQFreeMemory, the locate, security and instance functions) becomes a method with the same signature that calls `System.Messaging.Store.QueueStore`, messagingForCore's own code, which keeps local private queues in SQLite and returns MSMQ's status codes, fills the property buffers as MSMQ did and names what it doesn't support. The handles the marshaller made are made by the store (`FromStore`) | `Interop/UnsafeNativeMethods.cs`, `Interop/SafeNativeMethods.cs`, `Interop/NativeMethods.cs` (`MQGetSecurityContextEx`), `Interop/SafeHandles.cs` |
| **Asynchronous receive on the callback path** | `GetHandleInformation` returns false, so `BeginReceive`/`BeginPeek` use MSMQ's callback path, which Microsoft's code already has, instead of `ThreadPool.BindHandle` and overlapped I/O (Windows only). The store runs the receive on a thread of its own and calls the callback | `Interop/SafeNativeMethods.cs` |
| **No Kernel32 or Ole32** | `GetComputerName` is `Environment.MachineName`; `FormatMessage` finds nothing (MessageQueueException then uses its own "UnknownError" text); `OleLoadFromStream`/`OleSaveToStream` (ActiveXMessageFormatter with a COM object) throw, naming the limitation. The Advapi32 ACL calls stay, unreached: the store refuses queue security first | `Interop/SafeNativeMethods.cs`, `Interop/NativeMethods.cs` |
| **Limitations named** | An internal `MessageQueueException(int, string)`: MSMQ's message for the code, then what messagingForCore doesn't support and what to do | `MessageQueueException.cs` |
| **No DTC** | `Automatic` inside a `System.Transactions` scope used `TransactionInterop.GetDtcTransaction` (Windows, DTC); it throws the store's MessageQueueException (`TransactionUsage`). `Automatic` outside a scope is unchanged, and the store gives the message no transaction, as MSMQ did without a COM+ context | `MessageQueue.cs` |
| **BinaryMessageFormatter on webForCore's BinaryFormatter** | .NET's BinaryFormatter throws since .NET 9; the formatter uses webForCore's port (`webForCore.Serialization.Formatters`, `WebFormsForCore.Serialization.Formatters.Binary`), which keeps .NET Framework's format | `BinaryMessageFormatter.cs` |
| **Not compiled** | `MessageQueueInstaller` (needs System.Configuration.Install, which isn't on .NET; installutil only); `Design/QueuePathDialog` and `Design/QueuePathEditor` (Visual Studio's Windows Forms queue picker); `TelemetryEventSource.Events.cs` (below) | the project file |
| **No telemetry** | `MessageQueue`'s static constructor sent a usage event to Microsoft's telemetry provider; it's empty, and the event source isn't compiled | `MessageQueue.cs` |
| **No System.Configuration.Install** | `[InstallerType]` names `MessageQueueInstaller` as a string instead of `typeof` | `MessageQueue.cs` |
| **No Windows Forms** | The type converters' unused `System.Windows.Forms` usings are removed | `Design/MessageQueueConverter.cs`, `Design/MessageFormatterConverter.cs` |
| **Generated by the build** | .NET Framework's build generated `Res` (from `System.Messaging.txt`) and `AssemblyRef`. `Res.g.cs` is generated by `tools/generate-res.cs` and committed; the strings are compiled from the `.txt` at build time. `AssemblyRef.cs` is messagingForCore's, with .NET Framework 4.x's assembly names | `src/messagingForCore.System.Messaging/` |
| **Shared files** | The Reference Source's copies of `ExternDll`, `AppContextDefaultValues`, `LocalAppContext` and `InvariantComparer`, which .NET Framework's build compiled into System.Messaging | kept on the fork, unchanged |

Until the store, a DLL import resolver (`NoMsmq.cs`) refused `mqrt.dll`, because GitHub's Windows runners have MSMQ
installed and Microsoft's unmodified interop reached it. No `[DllImport(ExternDll.Mqrt)]` is left, so it's gone:
nothing calls MSMQ, on Windows either.

## Test changes, by kind

Tests change only to fit the platform, or where a test expected something .NET Framework doesn't do. A failing test
is never edited just to make it pass: either the library is fixed, or the failure is listed as known with its reason
(`tools/ci/known-failures/`).

| Kind | Change |
| --- | --- |
| **NUnit 3** | Mono's tests are NUnit 2. `[ExpectedException]` comes from webForCore's `NUnit2Compat.cs` (same semantics: the exact type) |
| **.NET Framework's behaviour** | `AsyncPeekTest` and `AsyncReceiveTest` built their messages with `new Message (new BinaryMessageFormatter ())`, which on .NET Framework is `Message (object body)`: the formatter became the body, and the message was sent with the default XmlMessageFormatter, which the queue's BinaryMessageFormatter can't read. They set the message's `Formatter` instead, as they mean. The callback tests wait for the callback, which .NET Framework calls after it sets the wait handle (`AsynchronousRequest.RaiseCompletionEvent`) |
| **Turned on** | `MessageEnumeratorTest`, which Mono ignored without a reason: enumerators are in scope, and it passes |
| **A database for the run** | `QueueDatabase.cs` (messagingForCore's) prepares a queue database for the run, as `messagingforcore-msmq install` does, and points the process at it |
