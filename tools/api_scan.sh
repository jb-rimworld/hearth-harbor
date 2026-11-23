#!/bin/bash
# Checks every [HarmonyPatch] target of the built DLLs against each RimWorld version's Assembly-CSharp.
# Usage: tools/api_scan.sh [1.7 1.6 1.5]   (build first: Source/build.sh all). Writes a summary per version, exit 1 if any is missing.
export PATH="$HOME/.dotnet:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
cd "$(dirname "$0")/.."
G17="$HOME/Library/Application Support/Steam/steamapps/common/RimWorld/RimWorld.app/Contents/MacOS/Assembly-CSharp.dll"
dotnet build tools/ApiScan/ApiScan.csproj -c Release -o tools/ApiScan/bin -v q -nologo 2>&1 | grep -E "error" | head
vs=${@:-1.7 1.6 1.5}; rc=0
for v in $vs; do
  case $v in 1.7) ref="$G17";; 1.6) ref=$(ls ~/.nuget/packages/krafs.rimworld.ref/1.6.*/ref/net472/Assembly-CSharp.dll | tail -1);; 1.5) ref=$(ls ~/.nuget/packages/krafs.rimworld.ref/1.5.*/ref/net472/Assembly-CSharp.dll | tail -1);; esac
  export HH_SEARCH="$HOME/Library/Application Support/Steam/steamapps/workshop/content/294100/2009463077/$v/Assemblies:$(dirname "$ref")"
  echo "== RimWorld $v ($ref)"
  dotnet tools/ApiScan/bin/ApiScan.dll "$v/Assemblies/Hearth.dll" "$ref" $v/Assemblies/Silverline.dll $v/Assemblies/Hearth.Core.dll 2>&1 || rc=1
done
exit $rc
