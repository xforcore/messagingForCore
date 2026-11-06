#!/usr/bin/env bash
# Runs messagingForCore's test suites in Release after a build of messagingForCore.slnx (Release), as CI does, and
# fails only on failures that are not in tools/ci/known-failures/ (docs/TESTING.md).
# Usage: tools/ci/run-tests.sh [results-directory]
set -uo pipefail
cd "$(dirname "$0")/../.."
# The absolute path (realpath -m is GNU only, and macOS has BSD tools).
mkdir -p "${1:-TestResults}"
OUT=$(cd "${1:-TestResults}" && pwd)

status=0
trx=()
for s in System.Messaging.Tests System.Messaging.MonoTests; do
  # --blame: if the test host crashes, the log names the test that was running.
  dotnet test "test/messagingForCore.$s/messagingForCore.$s.csproj" -c Release --no-build --blame \
    --logger "trx;LogFileName=$s.trx" --results-directory "$OUT"
  dotnet run tools/ci/check_failures.cs -- "$OUT/$s.trx" "tools/ci/known-failures/$s.txt" || status=1
  trx+=("$OUT/$s.trx")
done
dotnet run tools/ci/trx_summary.cs -- "${trx[@]}" > "$OUT/summary.md" || true
exit $status
