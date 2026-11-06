#!/usr/bin/env bash
# Builds and runs tools/ci/package-smoke, an application that uses messagingForCore through its packages only, against
# the packages in <package folder> (with webForCore's feed and nuget.org for everything else). The queue database is
# prepared with the messagingForCore.Msmq tool package, as a host would.
# Usage: tools/ci/package-smoke.sh <package folder> <version>
set -euo pipefail
# pwd -W: the Windows form of the path under Git Bash, since it is written into nuget.config.
packages=$(cd "$1" && { pwd -W 2>/dev/null || pwd; }); version=$2
here=$(cd "$(dirname "$0")" && pwd)
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
cp -r "$here/package-smoke/." "$work"
cd "$work"
cat > nuget.config <<CONFIG
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="messagingForCore" value="$packages" />
    <add key="webForCore" value="https://xforcore.github.io/webForCore/index.json" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="messagingForCore"><package pattern="messagingForCore" /><package pattern="messagingForCore.*" /></packageSource>
    <packageSource key="webForCore"><package pattern="webForCore.*" /></packageSource>
    <packageSource key="nuget.org"><package pattern="*" /></packageSource>
  </packageSourceMapping>
</configuration>
CONFIG
dotnet tool install messagingForCore.Msmq --version "$version" --tool-path "$work/tool" --configfile nuget.config -v q
"$work/tool/messagingforcore-msmq" install "$work/db/queues.db"
"$work/tool/messagingforcore-msmq" create-queue '.\private$\smoke' --transactional --db "$work/db/queues.db"
dotnet build Smoke.csproj -c Release -p:MessagingForCoreVersion="$version" -nodeReuse:false -v q -nologo
MESSAGINGFORCORE_DB= dotnet bin/Release/net10.0/Smoke.dll no-database
MESSAGINGFORCORE_DB="$work/db/queues.db" dotnet bin/Release/net10.0/Smoke.dll
"$work/tool/messagingforcore-msmq" list --db "$work/db/queues.db"
