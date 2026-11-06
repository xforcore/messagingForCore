#!/usr/bin/env bash
# The .NET Framework 4.8 comparison of message bodies (issue #1, docs/TESTING.md): test/messagingForCore.Comparison,
# built for net48 (.NET Framework's System.Messaging) and net10.0 (messagingForCore's, through the queue store).
# - On Windows: .NET Framework writes bodies and messagingForCore reads them, then the reverse, and the bytes are
#   compared for information. The bodies .NET Framework wrote are left in <folder>/netfx (CI keeps them as an
#   artifact; the stored fixtures in test/messagingForCore.Comparison/netfx48 are a copy).
# - Everywhere: messagingForCore reads the stored fixtures that .NET Framework 4.8 wrote, and its own bodies.
# Usage: tools/ci/netfx-compare.sh <folder>
set -euo pipefail
cd "$(dirname "$0")/../.."
mkdir -p "$1"
out=$(cd "$1" && pwd)
project=test/messagingForCore.Comparison
dotnet build "$project/messagingForCore.Comparison.csproj" -c Release -nodeReuse:false -v q -nologo
port() { dotnet "$project/bin/Release/net10.0/messagingForCore.Comparison.dll" "$@"; }

port write "$out/port"
port read "$out/port"
echo "Bodies .NET Framework 4.8 wrote (stored fixtures):"
if [[ -d "$project/netfx48" ]]; then port read "$project/netfx48"; fi

if [[ "${OS:-}" == "Windows_NT" ]]; then
  netfx() { "$project/bin/Release/net48/messagingForCore.Comparison.exe" "$@"; }
  netfx write "$out/netfx"
  echo ".NET Framework's bodies, read by messagingForCore:"
  port read "$out/netfx"
  echo "messagingForCore's bodies, read by .NET Framework:"
  netfx read "$out/port"
  port compare "$out/netfx" "$out/port"
  if [[ -d "$project/netfx48" ]]; then port compare "$out/netfx" "$project/netfx48"; fi
fi
