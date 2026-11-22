#!/bin/bash
# Presentation run: builds the hotel in the test game, takes screenshots, copies them to docs/screenshots/.
# Usage: tools/showcase/run_showcase.sh [label]   (env MODDIR, TIMEOUT)
set -u
REPO="$(cd "$(dirname "$0")/../.." && pwd)"; MODDIR=${MODDIR:-$REPO}
LABEL=${1:-showcase1}
MODDIR=$MODDIR TIMEOUT=${TIMEOUT:-1500} "$REPO/tools/run_test.sh" showcase $LABEL
code=$?
mkdir -p "$REPO/docs/screenshots"
SRC=/private/tmp/claude-501/hh_tests/showcase_shots
echo "screenshots from: $SRC"
rm -f "$REPO"/docs/screenshots/*.png
for f in "$SRC"/[0-9][0-9]_*.png; do [ -f "$f" ] && sips -Z 1600 "$f" --out "$REPO/docs/screenshots/$(basename "$f")" >/dev/null; done
ls -la "$REPO/docs/screenshots"
exit $code
