#!/bin/bash
# In-game test run WITHOUT (or with a chosen subset of) DLC. Copy of run_test.sh owned by the DLC module.
#   NO_DLC=1 (default): only Core is active.   DLCS=royalty,biotech  enables just those (royalty,ideology,biotech,anomaly,odyssey).
#   NO_DLC=0 and no DLCS: all DLC like run_test.sh.
# In-game test run (RimWorld 1.7, isolated profile, never touches Julius' own game).
# Usage: tools/run_test.sh <suites|all> [label] [extra packageIds ...]
#   env MODDIR=<path to a HearthHarbor checkout> (default: this repo), TIMEOUT=seconds (default 900), BUILD=0 to skip the build
# Builds with --tests, copies the mod into a test slot (2 slots, waits if both busy), runs -quicktest,
# prints the result summary and the path of the full log. Exit code 0 = all suites passed.
set -u
NO_DLC=${NO_DLC:-1}; DLCS=${DLCS:-}
if [ "$NO_DLC" = 0 ] && [ -z "$DLCS" ]; then DLCS=royalty,ideology,biotech,anomaly,odyssey; fi
dlcids=""; dlcknown="<li>ludeon.rimworld.royalty</li><li>ludeon.rimworld.ideology</li><li>ludeon.rimworld.biotech</li><li>ludeon.rimworld.anomaly</li><li>ludeon.rimworld.odyssey</li>"  # all listed as known, or RimWorld auto-enables the "new" ones
for d in ${DLCS//,/ }; do dlcids="$dlcids ludeon.rimworld.$d"; done
REPO="$(cd "$(dirname "$0")/.." && pwd)"; MODDIR=${MODDIR:-$REPO}
SUITES=${1:?suites}; LABEL=${2:-run}; shift; shift 2>/dev/null || true
S=/private/tmp/claude-501/hh_tests; mkdir -p $S
G="$HOME/Library/Application Support/Steam/steamapps/common/RimWorld"
[ "${BUILD:-1}" = 1 ] && { "$MODDIR/Source/build.sh" 1.7 --tests >$S/build_$LABEL.txt 2>&1; if grep -q "error" $S/build_$LABEL.txt; then cat $S/build_$LABEL.txt | head -40; echo "BUILD FAILED"; exit 2; fi; }
# acquire one of 2 slots
slot=""
for i in $(seq 1 720); do
  for n in 1 2 3; do if mkdir $S/lock$n 2>/dev/null; then slot=$n; break 2; fi; done
  # stale lock (> 25 min) → free it
  for n in 1 2 3; do [ -d $S/lock$n ] && [ $(( $(date +%s) - $(stat -f %m $S/lock$n) )) -gt 1500 ] && rmdir $S/lock$n; done
  sleep 5
done
[ -z "$slot" ] && { echo "no free test slot"; exit 3; }
trap "rm -f '$G/Mods/_hh_slot$slot'; rmdir $S/lock$slot 2>/dev/null" EXIT
SD=$S/slot$slot; rm -rf $SD/mod; mkdir -p $SD/mod
rsync -a --delete --exclude Source --exclude .git --exclude tools --exclude docs --exclude _scratch "$MODDIR/" $SD/mod/
sed -i '' "s#<packageId>juliusberger00.hearthharbor</packageId>#<packageId>juliusberger00.hearthharbor.slot$slot</packageId>#" $SD/mod/About/About.xml
sed -i '' '/<incompatibleWith>/,/<\/incompatibleWith>/d' $SD/mod/About/About.xml
ln -sfn $SD/mod "$G/Mods/_hh_slot$slot"
FH=$SD/home; C="$FH/Library/Application Support/RimWorld/Config"; rm -rf "$FH"; mkdir -p "$C"
cp "$HOME/Library/Application Support/RimWorld/Config/Prefs.xml" "$C/" 2>/dev/null
V=$(sed -n 's/.*<version>\(.*\)<\/version>.*/\1/p' "$HOME/Library/Application Support/RimWorld/Config/ModsConfig.xml")
{ echo '<?xml version="1.0" encoding="utf-8"?><ModsConfigData><version>'$V'</version><activeMods>'
  for p in brrainz.harmony ludeon.rimworld $dlcids "$@" juliusberger00.hearthharbor.slot$slot; do echo "<li>$p</li>"; done
  echo "</activeMods><knownExpansions>$dlcknown</knownExpansions></ModsConfigData>"; } > "$C/ModsConfig.xml"
OUT=$S/result_$LABEL.txt; LOG=$S/player_$LABEL.log; rm -f $OUT
cd "$G/RimWorld.app/Contents/MacOS"
# test instances run without a window (engine offscreen backend, virtual 1920x1080 screen) and muted; see _Tools/rwquiet/README.md
sed -i '' -e 's#<volumeMaster>[^<]*</volumeMaster>#<volumeMaster>0</volumeMaster>#' "$FH/Library/Application Support/RimWorld/Config/Prefs.xml" 2>/dev/null
HOME=$FH HH_TEST="$SUITES" HH_TEST_OUT=$OUT timeout ${TIMEOUT:-900} env WAYLITH_BACKEND=offscreen ./RimWorld -quicktest > $LOG 2>&1
code=$?
cp "$C/HearthHarbor.log" $S/hh_$LABEL.log 2>/dev/null
echo "slot $slot · exit $code · log $LOG · hh-log $S/hh_$LABEL.log"
grep -E "^\[HHTEST\]|Exception|\[Hearth&Harbor\].*(ERROR|rror)" $LOG | grep -v "closing window" | head -60
echo "--- red errors in log: $(grep -c -E '^(Exception|.*Error|.*error CS)' $LOG)"
if [ -f $OUT ]; then cat $OUT | head -120; grep -q "^RESULT PASS" $OUT && exit 0; exit 1; else echo "NO RESULT FILE (crash or timeout)"; tail -30 $LOG; exit 4; fi
