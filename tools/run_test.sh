#!/bin/bash
# In-game test run (RimWorld 1.7, isolated profile, never touches Julius' own game).
# Usage: tools/run_test.sh <suites|all> [label] [extra packageIds ...]
#   env MODDIR=<path to a HearthHarbor checkout> (default: this repo), TIMEOUT=seconds (default 900), BUILD=0 to skip the build
# Save/load runs (tools/run_saveload.sh drives these; any of them adds the test-only probe mod tools/probe):
#   LOAD=<dir>/<name>.rws  copy that save (and every file next to it) into the test profile and load it at start instead of -quicktest
#                          (vanilla dev-mode "autostart" save); suites see HH_TEST_LOAD=<name>
#   GAME_LANG="German (Deutsch)"  start the test profile in that language (folder name)
#   NO_HH=1                run WITHOUT Hearth & Harbor (the probe drives the game, needs PROBE_DAYS)
#   PROBE_DAYS=<n>         the probe plays n days, writes a census and quits (use with NO_HH=1 or an empty suite list "-")
#   PROBE_SAVE=<name>      the probe saves the game as <name> before quitting
#   SAVES_OUT=<dir>        after the run, copy the profile's Saves folder there (saves made by suites or the probe)
#   The probe records every red error of the run in $S/probe_err_<label>.txt (columns: load|play, type, first line).
# Builds with --tests, copies the mod into a test slot (2 slots, waits if both busy), runs -quicktest,
# prints the result summary and the path of the full log. Exit code 0 = all suites passed.
set -u
REPO="$(cd "$(dirname "$0")/.." && pwd)"; MODDIR=${MODDIR:-$REPO}
SUITES=${1:?suites}; LABEL=${2:-run}; shift; shift 2>/dev/null || true
[ "$SUITES" = "-" ] && SUITES=""
LOAD=${LOAD:-}; NO_HH=${NO_HH:-0}; PROBE_DAYS=${PROBE_DAYS:-}; PROBE_SAVE=${PROBE_SAVE:-}; SAVES_OUT=${SAVES_OUT:-}
PROBE=0; [ -n "$LOAD$PROBE_DAYS$PROBE_SAVE$SAVES_OUT" -o "$NO_HH" = 1 ] && PROBE=1
S=/private/tmp/claude-501/hh_tests; mkdir -p $S
G="$HOME/Library/Application Support/Steam/steamapps/common/RimWorld"
[ "${BUILD:-1}" = 1 ] && { "$MODDIR/Source/build.sh" 1.7 --tests >$S/build_$LABEL.txt 2>&1; if grep -q "error" $S/build_$LABEL.txt; then cat $S/build_$LABEL.txt | head -40; echo "BUILD FAILED"; exit 2; fi; }
if [ $PROBE = 1 ] && [ "${BUILD:-1}" = 1 ]; then
  (export PATH="$HOME/.dotnet:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1; dotnet build "$MODDIR/tools/probe/HHProbe.csproj" -c Release -v q -nologo 2>&1 | grep -E " error " | sort -u) > $S/build_probe_$LABEL.txt
  [ -s $S/build_probe_$LABEL.txt ] && { cat $S/build_probe_$LABEL.txt; echo "PROBE BUILD FAILED"; exit 2; }
fi
# acquire one of 3 slots
slot=""
for i in $(seq 1 720); do
  for n in 1 2 3; do if mkdir $S/lock$n 2>/dev/null; then slot=$n; break 2; fi; done
  # stale lock (> 25 min) → free it
  for n in 1 2 3; do [ -d $S/lock$n ] && [ $(( $(date +%s) - $(stat -f %m $S/lock$n) )) -gt 1500 ] && rmdir $S/lock$n; done
  sleep 5
done
[ -z "$slot" ] && { echo "no free test slot"; exit 3; }
trap "rm -f '$G/Mods/_hh_slot$slot' '$G/Mods/_hh_probe$slot'; rmdir $S/lock$slot 2>/dev/null" EXIT
SD=$S/slot$slot; rm -rf $SD/mod; mkdir -p $SD/mod
rsync -a --delete --exclude Source --exclude .git --exclude tools --exclude docs --exclude _scratch "$MODDIR/" $SD/mod/
sed -i '' "s#<packageId>juliusberger00.hearthharbor</packageId>#<packageId>juliusberger00.hearthharbor.slot$slot</packageId>#" $SD/mod/About/About.xml
sed -i '' '/<incompatibleWith>/,/<\/incompatibleWith>/d' $SD/mod/About/About.xml
ln -sfn $SD/mod "$G/Mods/_hh_slot$slot"
probeid=""
if [ $PROBE = 1 ]; then
  rm -rf $SD/probe; mkdir -p $SD/probe/About $SD/probe/1.7; cp -R "$MODDIR/tools/probe/bin/1.7/Assemblies" $SD/probe/1.7/
  probeid=juliusberger00.hhprobe.slot$slot
  printf '<?xml version="1.0" encoding="utf-8"?>\n<ModMetaData><name>HH save probe (tests)</name><author>juliusberger00</author><packageId>%s</packageId><supportedVersions><li>1.7</li></supportedVersions><description>Test-only.</description></ModMetaData>\n' $probeid > $SD/probe/About/About.xml
  ln -sfn $SD/probe "$G/Mods/_hh_probe$slot"
fi
hhid=juliusberger00.hearthharbor.slot$slot; [ "$NO_HH" = 1 ] && hhid=""
FH=$SD/home; C="$FH/Library/Application Support/RimWorld/Config"; rm -rf "$FH"; mkdir -p "$C"
cp "$HOME/Library/Application Support/RimWorld/Config/Prefs.xml" "$C/" 2>/dev/null
[ -n "${GAME_LANG:-}" ] && sed -i '' -e "s#<langFolderName>[^<]*</langFolderName>#<langFolderName>$GAME_LANG</langFolderName>#" "$C/Prefs.xml"
# hidden test windows never draw the autosave screen in 1.7 → the game would stall at the first autosave; tests never autosave
sed -i '' -e "s#<autosaveIntervalDays>[^<]*</autosaveIntervalDays>#<autosaveIntervalDays>1000</autosaveIntervalDays>#" -e "s#<runInBackground>[^<]*</runInBackground>#<runInBackground>True</runInBackground>#" "$C/Prefs.xml" 2>/dev/null
# loading a save: vanilla loads Saves/autostart.rws at start in dev mode; no pause on load (the first tick would come before the suites look)
if [ -n "$LOAD" ]; then
  sed -i '' -e "s#<devMode>[^<]*</devMode>#<devMode>True</devMode>#" -e "s#<pauseOnLoad>[^<]*</pauseOnLoad>#<pauseOnLoad>False</pauseOnLoad>#" "$C/Prefs.xml"
  SV="$FH/Library/Application Support/RimWorld/Saves"; mkdir -p "$SV"; cp "$(dirname "$LOAD")"/* "$SV/"; cp "$LOAD" "$SV/autostart.rws"
  export HH_TEST_LOAD=$(basename "$LOAD" .rws)
fi
V=$(sed -n 's/.*<version>\(.*\)<\/version>.*/\1/p' "$HOME/Library/Application Support/RimWorld/Config/ModsConfig.xml")
{ echo '<?xml version="1.0" encoding="utf-8"?><ModsConfigData><version>'$V'</version><activeMods>'
  for p in brrainz.harmony ludeon.rimworld ludeon.rimworld.royalty ludeon.rimworld.ideology ludeon.rimworld.biotech ludeon.rimworld.anomaly ludeon.rimworld.odyssey "$@" $hhid $probeid; do echo "<li>$p</li>"; done
  echo '</activeMods><knownExpansions><li>ludeon.rimworld.royalty</li><li>ludeon.rimworld.ideology</li><li>ludeon.rimworld.biotech</li><li>ludeon.rimworld.anomaly</li><li>ludeon.rimworld.odyssey</li></knownExpansions></ModsConfigData>'; } > "$C/ModsConfig.xml"
OUT=$S/result_$LABEL.txt; LOG=$S/player_$LABEL.log; PERR=$S/probe_err_$LABEL.txt; POUT=$S/probe_$LABEL.txt; rm -f $OUT $PERR $POUT
QT=-quicktest; [ -n "$LOAD" ] && QT=""
[ $PROBE = 1 ] && export HH_PROBE_ERR=$PERR HH_PROBE_OUT=$POUT
[ -n "$PROBE_DAYS" ] && export HH_PROBE_DAYS=$PROBE_DAYS
[ -n "$PROBE_SAVE" ] && export HH_PROBE_SAVE=$PROBE_SAVE
cd "$G/RimWorld.app/Contents/MacOS"
# test instances run without a window (engine offscreen backend, virtual 1920x1080 screen) and muted; see _Tools/rwquiet/README.md
sed -i '' -e 's#<volumeMaster>[^<]*</volumeMaster>#<volumeMaster>0</volumeMaster>#' "$FH/Library/Application Support/RimWorld/Config/Prefs.xml" 2>/dev/null
HOME=$FH HH_TEST="$SUITES" HH_TEST_OUT=$OUT timeout ${TIMEOUT:-900} env WAYLITH_BACKEND=offscreen ./RimWorld $QT > $LOG 2>&1
code=$?
if [ -n "$SAVES_OUT" ]; then mkdir -p "$SAVES_OUT"; cp "$FH/Library/Application Support/RimWorld/Saves/"* "$SAVES_OUT/" 2>/dev/null; rm -f "$SAVES_OUT/autostart.rws"; fi
cp "$C/HearthHarbor.log" $S/hh_$LABEL.log 2>/dev/null
echo "slot $slot · exit $code · log $LOG · hh-log $S/hh_$LABEL.log"
grep -E "^\[HHTEST\]|Exception|\[Hearth&Harbor\].*(ERROR|rror)" $LOG | grep -v "closing window" | head -60
echo "--- red errors in log: $(grep -c -E '^(Exception|.*Error|.*error CS)' $LOG)"
if [ $PROBE = 1 ]; then
  echo "--- probe: $(wc -l < $PERR 2>/dev/null | tr -d ' ') red error line(s) in $PERR"; [ -f $POUT ] && cat $POUT
fi
if [ -z "$SUITES" ]; then [ -f $POUT ] && grep -q "^PROBE DONE" $POUT && exit 0; echo "PROBE DID NOT FINISH"; tail -30 $LOG; exit 4; fi
if [ -f $OUT ]; then cat $OUT | head -120; grep -q "^RESULT PASS" $OUT && exit 0; exit 1; else echo "NO RESULT FILE (crash or timeout)"; tail -30 $LOG; exit 4; fi
