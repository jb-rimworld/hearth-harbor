#!/bin/bash
# Baut Hearth & Harbor. Aufruf: Source/build.sh [1.7|1.6|1.5|all] [--tests]
#   --tests  baut mit dem eingebauten Testgerüst (HH_TESTS), nie für Veröffentlichungen
set -e
export PATH="$HOME/.dotnet:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
cd "$(dirname "$0")"
V=${1:-1.7}; T=false; [ "$2" = "--tests" -o "$1" = "--tests" ] && T=true; [ "$V" = "--tests" ] && V=1.7
vs=$V; [ "$V" = all ] && vs="1.7 1.6 1.5"
for v in $vs; do
  echo "== RimWorld $v (tests=$T)"
  dotnet build Hearth/Hearth.csproj -c Release -p:RimVersion=$v -p:HHTests=$T -v q -nologo 2>&1 | grep -oE "Source/[^ ]*\): error [A-Z0-9]+: .*" | sed "s# \[/.*##" | sort -u | head -80 || true
  ls -la ../$v/Assemblies/*.dll 2>/dev/null | awk '{print "   ", $5, $9}'
done
