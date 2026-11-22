#!/bin/bash
export PATH="$HOME/.dotnet:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
cd "$(dirname "$0")" && dotnet test Hearth.Core.Tests.csproj -p:RimVersion=1.7 --nologo -v q 2>&1 | grep -E "error|Bestanden|Passed|Failed|Fehler|failed|Total|Gesamt" | head -40
