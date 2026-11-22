#!/bin/bash
# Build with visible compiler errors (Source/build.sh hides messages): tools/hb.sh
export PATH="$HOME/.dotnet:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1
cd "$(dirname "$0")/../Source" && dotnet build Hearth/Hearth.csproj -c Release -p:RimVersion=${1:-1.7} -p:HHTests=${2:-true} -nologo -v q 2>&1 | grep -E "error|warning CS" | sed -E "s#/Users/[^ ]*/Source/##; s#\[/Users[^]]*\]##" | sort -u | head -${3:-60}
echo "build done"
