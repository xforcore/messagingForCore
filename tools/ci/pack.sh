#!/usr/bin/env bash
# Packs messagingForCore's NuGet packages after a Release build of messagingForCore.slnx.
# Usage: tools/ci/pack.sh <version> <output folder>
set -euo pipefail
cd "$(dirname "$0")/../.."
version=$1
# The absolute path (realpath -m is GNU only, and macOS has BSD tools).
mkdir -p "$2"
out=$(cd "$2" && pwd)
for project in src/*/*.csproj; do
  dotnet pack "$project" -c Release --no-build -nodeReuse:false -p:Version="$version" -o "$out" -v q -nologo
done
echo "$(ls "$out"/*.nupkg | wc -l) packages in $out"
