#!/bin/bash
# Save/load scenarios (KONZEPT 3.7, 15.5 removal/addmidgame, 15.7). Each scenario is two or more game starts in a row;
# a later start loads the save of an earlier one, sometimes with a different mod list. Uses tools/run_test.sh (3 shared slots).
# Usage: tools/run_saveload.sh <label> [roundtrip removal addition modules | all]   (default all)
#   env MODDIR as for run_test.sh. Saves and side files: /private/tmp/claude-501/hh_tests/saveload_<label>/
# Phases (independent phases run in parallel):
#   build      quicktest + H&H: demo hotel, guests, bank, loan, policies, investments, lottery, casino session, underworld → hh_roundtrip
#   roundtrip  load hh_roundtrip with the same mods: fingerprint equal, facts equal, 2 days clean
#   raw        load hh_roundtrip WITHOUT H&H (probe plays 1 day): which vanilla errors appear
#   prep       load hh_roundtrip, every module's PrepareRemoval → hh_prepared;  rawprep: load hh_prepared WITHOUT H&H, 1 day
#   vanilla    quicktest WITHOUT H&H → hh_vanilla;  addition: load hh_vanilla WITH H&H
#   off        load hh_roundtrip, switch modules off → hh_modules_off;  on: load it, check, switch on, 1 day
set -u
REPO="$(cd "$(dirname "$0")/.." && pwd)"; export MODDIR=${MODDIR:-$REPO}
LABEL=${1:?label}; shift; WHAT=${*:-all}; [ "$WHAT" = all ] && WHAT="roundtrip removal addition modules"
has() { [[ " $WHAT " == *" $1 "* ]]; }
S=/private/tmp/claude-501/hh_tests; D=$S/saveload_$LABEL; mkdir -p $D
T="$MODDIR/tools/run_test.sh"
"$MODDIR/Source/build.sh" 1.7 --tests > $D/build.txt 2>&1; grep -q "error" $D/build.txt && { cat $D/build.txt; exit 2; }
(export PATH="$HOME/.dotnet:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1; dotnet build "$MODDIR/tools/probe/HHProbe.csproj" -c Release -v q -nologo 2>&1 | grep -E " error ") && exit 2
export BUILD=0
phase() { # name, then env assignments and run_test.sh args; output in $D/<name>.out
  local n=$1; shift; echo "[$(date +%H:%M:%S)] start $n"; env "$@" > $D/$n.out 2>&1; local c=$?; echo "[$(date +%H:%M:%S)] end $n exit $c"; echo $c > $D/$n.code; }
RT=$D/build/hh_roundtrip.rws
if [ ! -f $RT ] || [ "${REBUILD:-0}" = 1 ]; then
  phase build SAVES_OUT=$D/build $T saveload.build ${LABEL}_build
  [ -f $RT ] || { echo "phase build made no save"; tail -40 $D/build.out; exit 1; }
fi
has roundtrip && phase roundtrip LOAD=$RT SAVES_OUT=$D/roundtrip $T saveload.roundtrip ${LABEL}_roundtrip &
has removal && { phase raw LOAD=$RT NO_HH=1 PROBE_DAYS=1 $T - ${LABEL}_raw; } &
has removal && { phase prep LOAD=$RT SAVES_OUT=$D/prep $T saveload.prepare ${LABEL}_prep && phase rawprep LOAD=$D/prep/hh_prepared.rws NO_HH=1 PROBE_DAYS=1 $T - ${LABEL}_rawprep; } &
wait
has addition && { phase vanilla NO_HH=1 PROBE_DAYS=0 PROBE_SAVE=hh_vanilla SAVES_OUT=$D/vanilla $T - ${LABEL}_vanilla && phase addition LOAD=$D/vanilla/hh_vanilla.rws SAVES_OUT=$D/addition $T saveload.addition ${LABEL}_addition; } &
has modules && { phase off LOAD=$RT SAVES_OUT=$D/off $T saveload.modulesoff ${LABEL}_off && phase on LOAD=$D/off/hh_modules_off.rws SAVES_OUT=$D/on $T saveload.moduleson ${LABEL}_on; } &
wait
echo "================ summary ($D)"
rc=0
for n in build roundtrip raw prep rawprep vanilla addition off on; do
  [ -f $D/$n.out ] || continue
  c=$(cat $D/$n.code 2>/dev/null || echo "?"); [ "$c" = 0 ] || rc=1
  echo "== $n: exit $c"; grep -E "^RESULT|^## |FAIL|^--- probe|^(==|colonists|friendly|silver|areas|PROBE)" $D/$n.out | head -40
  f=$S/probe_err_${LABEL}_$n.txt
  [ -s "$f" ] && { echo "   red errors (count × phase/type/text):"; sort "$f" | uniq -c | sort -rn | head -25 | sed 's/^/   /'; }
done
exit $rc
