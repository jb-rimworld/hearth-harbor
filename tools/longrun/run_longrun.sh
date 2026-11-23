#!/bin/bash
# Long run: 15 in-game days of the showcase hotel at maximum speed (suite "longrun"), CSV + markdown report copied to docs/longrun/.
# Usage: tools/longrun/run_longrun.sh [label]   (env MODDIR, TIMEOUT default 3000, HH_LONGRUN_DAYS default 15)
set -u
REPO="$(cd "$(dirname "$0")/../.." && pwd)"; MODDIR=${MODDIR:-$REPO}
LABEL=${1:-longrun1}
MODDIR=$MODDIR TIMEOUT=${TIMEOUT:-3000} "$REPO/tools/run_test.sh" longrun $LABEL
code=$?
S=/private/tmp/claude-501/hh_tests
mkdir -p "$REPO/docs/longrun"
for e in csv md; do [ -f "$S/longrun_$LABEL.$e" ] && cp "$S/longrun_$LABEL.$e" "$REPO/docs/longrun/longrun.$e"; done
ls -la "$REPO/docs/longrun"
exit $code
