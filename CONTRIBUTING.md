# Contributing to messagingForCore

messagingForCore runs Microsoft's System.Messaging on .NET 10, with local queues stored in SQLite instead of MSMQ. A
change is welcome when it brings System.Messaging closer to what it does on .NET Framework 4.8 with MSMQ, within the
scope in [issue #1](https://github.com/xforcore/messagingForCore/issues/1), and when it can prove it with a test.

## The principles

1. **.NET Framework 4.8 with MSMQ is the measure.** Within the scope, an application sees what it saw on .NET Framework:
   the same results, the same exceptions and the same `MessageQueueErrorCode`s. Outside the scope, it gets a
   `MessageQueueException` that names the limitation, never a silent difference.

2. **Every change comes with a test.** Where Mono's suite covers the behaviour, that is the test: a known failure that
   now passes, removed from `tools/ci/known-failures/` in the same change (CI reports known failures that pass).
   Otherwise add messagingForCore's own test, and say in the pull request that it failed before the change.

3. **Avoid changing Microsoft's code.** System.Messaging comes unchanged from the `external/` submodules apart from a
   short list of changes ([`docs/MODIFICATIONS.md`](docs/MODIFICATIONS.md)). Its value is that it *is* Microsoft's
   code: its API, argument checks, property handling and exceptions. The queue store goes behind the interop layer
   (`Interop/UnsafeNativeMethods.cs`, `Interop/SafeNativeMethods.cs`), where the MSMQ calls are, and is
   messagingForCore's own code.

4. **When Microsoft's code or Mono's tests must change, keep the record.** Keep the change as small as it can be and:
   - make it a commit on the fork's `messagingforcore` branch (`xforcore/referencesource` or `xforcore/mono`), one
     change per commit, with a `// messagingForCore:` comment and a message that say why;
   - move the submodule to it in a messagingForCore pull request, with the test;
   - add the kind of change to [`docs/MODIFICATIONS.md`](docs/MODIFICATIONS.md) if it's a new kind.

   A Mono test changes only to fit .NET or NUnit 3, or where Mono's expectation differs from .NET Framework's. A
   failing test is never edited just to make it pass.

5. **The project files are messagingForCore's.** They compile the submodules' files through `Link` items at their
   old paths, next to messagingForCore's own files, and are edited by hand.

6. **Report numbers.** A change says what the suites gave before and after: tests run, passed, failed and skipped.

7. **Every change names what needs it.** Say which application hit the problem, what it did and what its user saw,
   or which of Mono's tests or which .NET Framework behaviour records it. Synthetic edge cases are fine as extra tests,
   but the reason for a change is an application or .NET Framework's recorded behaviour.

## How messagingForCore's own code is written

These are webForCore's rules for its own code (its CONTRIBUTING.md), applied here to the queue store and the tools.
They don't apply to Microsoft's code in the submodules (principle 3 keeps that as it is).

8. **No reflection into internals.** Don't read, write or call non-public members of the runtime, of
   Microsoft.Data.Sqlite, or of Microsoft's System.Messaging types, and don't depend on their private names or layout.

9. **No unsafe code and no GC tricks, except at the native boundary.** The store stands in for `mqrt.dll`, so its
   functions take what Microsoft's code passes to MSMQ: property ID arrays and PROPVARIANT buffers
   (`Interop/MessagePropertyVariants.cs`). Reading and writing those in the function that receives them is the
   native contract, and is allowed there only: convert to managed types at that edge. Nothing that steers the garbage
   collector: no `GC.Collect`, `GC.WaitForPendingFinalizers` or `GC.AddMemoryPressure`, no `GCHandle`s of the store's
   own, no polling weak references.

10. **Slow, secure and stable beats fast.** Choose the simplest design that is clearly correct. Every wait has a
    timeout. When performance does need work, the pull request includes the measurement.

11. **Programs and tools are .NET.** Everything this repository builds, and every tool it uses, is written in C# as a
    project or as a file-based app run with `dotnet run tool.cs`. Shell scripts (bash, PowerShell) and CI YAML are
    fine as glue that calls dotnet, git, gh and curl.

## Pull requests

- One problem per commit, with `Fixes #<issue>` in the message where there is one.
- The pull request lists, for each change: the problem, what needs it (principle 7), the fix, the test and the suites'
  numbers (principle 6), and whether Microsoft's code or Mono's tests changed (and where that is documented).
- CI (`.github/workflows/ci.yml`) builds, runs the tests, packs the packages and runs the package smoke test, on Linux
  and on Windows; both must pass.

## Releases

1. Set `<Version>` in `Directory.Build.props` and write `docs/release-notes/<version>.md`.
2. Update the test numbers in README.md and docs/TESTING.md.
3. Tag the merged commit `v<version>`. CI builds and tests the tag on both platforms and creates the GitHub release
   with the packages.

Licences: messagingForCore's own code is MIT, and Microsoft's and Mono's code keeps its MIT licence
([`docs/LICENSING.md`](docs/LICENSING.md)). Contributions are accepted under the MIT License.
