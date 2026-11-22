#!/bin/bash
# Runs every module's suites in its own game instance (no cross-suite state), up to 3 in parallel.
# Usage: tools/test_matrix.sh [label-prefix] [group ...]   default groups: all modules
# Result summary: /private/tmp/claude-501/hh_tests/matrix_<prefix>.txt
R="$(cd "$(dirname "$0")/.." && pwd)"; P=${1:-m}; shift
G=${@:-"core inn venues kitchen casino bank insurance ui wellness city underworld hospcompat finance dlc"}
"$R/Source/build.sh" 1.7 --tests | grep -E "error" && exit 2
cd "$R" && uv run --with pillow python3 tools/placeholder_textures.py >/dev/null
S=/private/tmp/claude-501/hh_tests; OUT=$S/matrix_$P.txt; : > $OUT
for g in $G; do
  q="$g.*"; [ $g = core ] && q="smoke"
  ( BUILD=0 TIMEOUT=1500 "$R/tools/run_test.sh" "smoke,$q" "${P}_$g" > $S/matrix_${P}_$g.out 2>&1; echo "$g: $(grep -m1 -E '^RESULT|NO RESULT' $S/matrix_${P}_$g.out) · red errors $(grep -m1 'red errors' $S/matrix_${P}_$g.out | grep -oE '[0-9]+$')" >> $OUT ) &
  sleep 20
  while [ $(jobs -r | wc -l) -ge 3 ]; do sleep 5; done
done
wait
sort $OUT; echo "details: $S/result_${P}_<group>.txt"
