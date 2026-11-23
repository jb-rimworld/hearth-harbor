#!/bin/bash
# Smoke-load other mods WITHOUT Hearth & Harbor: quicktest for N seconds, then report red errors.
# Usage: tools/run_plain.sh <label> <seconds> <packageIds...>
S=/private/tmp/claude-501/hh_tests; mkdir -p $S; L=$1; T=$2; shift 2
G="$HOME/Library/Application Support/Steam/steamapps/common/RimWorld"
slot=""; for i in $(seq 1 720); do for n in 1 2 3; do if mkdir $S/lock$n 2>/dev/null; then slot=$n; break 2; fi; done; sleep 5; done
trap "rmdir $S/lock$slot 2>/dev/null" EXIT
FH=$S/plain_$L; C="$FH/Library/Application Support/RimWorld/Config"; rm -rf "$FH"; mkdir -p "$C"
cp "$HOME/Library/Application Support/RimWorld/Config/Prefs.xml" "$C/"; sed -i '' -e "s#<autosaveIntervalDays>[^<]*<#<autosaveIntervalDays>1000<#" "$C/Prefs.xml"
V=$(sed -n 's/.*<version>\(.*\)<\/version>.*/\1/p' "$HOME/Library/Application Support/RimWorld/Config/ModsConfig.xml")
{ echo '<?xml version="1.0" encoding="utf-8"?><ModsConfigData><version>'$V'</version><activeMods>'
  for p in brrainz.harmony ludeon.rimworld ludeon.rimworld.royalty ludeon.rimworld.ideology ludeon.rimworld.biotech ludeon.rimworld.anomaly ludeon.rimworld.odyssey "$@"; do echo "<li>$p</li>"; done
  echo '</activeMods><knownExpansions><li>ludeon.rimworld.royalty</li><li>ludeon.rimworld.ideology</li><li>ludeon.rimworld.biotech</li><li>ludeon.rimworld.anomaly</li><li>ludeon.rimworld.odyssey</li></knownExpansions></ModsConfigData>'; } > "$C/ModsConfig.xml"
cd "$G/RimWorld.app/Contents/MacOS"; HOME=$FH timeout $T ./RimWorld -quicktest > $S/plain_$L.log 2>&1
echo "log $S/plain_$L.log"; grep -c "Map generated\|Started new game\|quicktest" $S/plain_$L.log
grep -B1 "at Verse.Log.Error" $S/plain_$L.log | grep -v "   at \|^--" | sed 's/\x1b\[[0-9;]*m//g' | sort | uniq -c | head -20
