# Licensing

A plain-language summary. [NOTICE](../NOTICE) is the authoritative list of components, sources and copyright holders.
This isn't legal advice.

## Can I use messagingForCore in a commercial product?

Yes. Everything in the packages is under the MIT License.

| Part | Where | Licence | Commercial use |
| --- | --- | --- | --- |
| System.Messaging (Microsoft's code, .NET Framework 4.8 Reference Source) | `external/referencesource`, built by `src/messagingForCore.System.Messaging` | MIT (© Microsoft) | ✅ Yes |
| messagingForCore's own code | `src/`, `tools/` | MIT (© messagingForCore contributors) | ✅ Yes |
| System.Security.Permissions, System.DirectoryServices (from NuGet) | restored at build time | MIT (© .NET Foundation) | ✅ Yes |
| Mono's System.Messaging tests | `external/mono`, built by `test/` | MIT (© their authors) | ✅ Yes (not in the packages) |

### What the licences ask of you

| Licence | When you distribute the code or binaries (including inside a commercial product) |
| --- | --- |
| MIT | Include the copyright notice and the licence text |

## Names and signatures

The assembly keeps Microsoft's name (System.Messaging), version (4.0.0.0) and public key, so that code compiled
against it binds without changes. It is public signed with that public key, not signed by Microsoft, and
messagingForCore isn't affiliated with or endorsed by Microsoft.

Public signing is the SDK's (`SignAssembly` and `PublicSign` in the project), as drawingForCore and dotnet/runtime's
own open-source builds do it: `keys/System.Messaging.snk` holds only Microsoft's public key, so the assembly carries
public key token b03f5f7f11d50a3a, which the package smoke test checks. No SHA-1 signing happens, so no OpenSSL setting
is needed to build.

## Telemetry

.NET Framework's System.Messaging sent a usage event to Microsoft's telemetry provider the first time an application
used `MessageQueue`. messagingForCore doesn't compile that code ([MODIFICATIONS.md](MODIFICATIONS.md)) and sends
nothing.
