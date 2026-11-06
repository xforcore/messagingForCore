# Where Microsoft's code comes from

messagingForCore's System.Messaging is Microsoft's, from the .NET Framework 4.8 Reference Source, and only from there
(not from dotnet/runtime, which has no System.Messaging). Mono's tests come from mono/mono. Both are submodules of
xforcore forks; [MODIFICATIONS.md](MODIFICATIONS.md) lists every change.

## The lift

| | System.Messaging | Mono's tests |
| --- | --- | --- |
| Upstream | [microsoft/referencesource](https://github.com/microsoft/referencesource) `ec9fa9a` (main, .NET Framework 4.8) | [mono/mono](https://github.com/mono/mono) `0f53e9e` (main) |
| Fork and branch | [xforcore/referencesource](https://github.com/xforcore/referencesource) `messagingforcore` (beside webForCore's `webforcore`) | [xforcore/mono](https://github.com/xforcore/mono) `messagingforcore` (beside `webforcore`) |
| Submodule | `external/referencesource` (shallow) | `external/mono` (shallow) |
| Kept | `System.Messaging/` (74 `.cs` files, the strings in `System.Messaging.txt`, three bitmaps) and the four shared files .NET Framework's build compiled in, with the licences | `mcs/class/System.Messaging/Test/` (10 files), with the licences |
| Compiled by | `src/messagingForCore.System.Messaging` (70 files: not `MessageQueueInstaller`, `Design/QueuePathDialog`, `Design/QueuePathEditor`, `TelemetryEventSource.Events`) | `test/messagingForCore.System.Messaging.MonoTests` (all 10, Mono's `System.Messaging_test.dll.sources` list) |

Each branch is the upstream commit, then one commit that removes everything else ("keep only the files messagingForCore
uses"), then messagingForCore's changes, one file per commit, each marked `// messagingForCore:`. The project files
are messagingForCore's: they compile the submodule's files through `Link` items at their old paths, next to
messagingForCore's own files (the queue store, `Res.g.cs`, `AssemblyRef.cs`).

## The edits, in short

All of MSMQ is reached through the interop layer, `Interop/UnsafeNativeMethods.cs` and `Interop/SafeNativeMethods.cs`
(and `MQGetSecurityContextEx` in `Interop/NativeMethods.cs`): the store replaces those functions and nothing above
them changes. The few edits outside it are the ones issue #1 named (asynchronous receive, `Automatic` transactions,
`BinaryMessageFormatter`) and the limitation message on `MessageQueueException`.

## Changing the source

- **A change to Microsoft's code or Mono's tests:** commit it on the fork's `messagingforcore` branch (one file per
  commit, `// messagingForCore:` comment, the reason in the message), push the branch (fast-forward only), and move
  the submodule in a messagingForCore pull request with the test.
- **A newer upstream:** microsoft/referencesource is frozen at .NET Framework 4.8; there is nothing to follow. For
  Mono, rebase the branch's commits onto the new commit on a new branch, and record the new commit here and in
  MODIFICATIONS.md.
