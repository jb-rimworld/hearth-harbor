#!/bin/bash
# Screenshots of The Lucky Thrumbo -> docs/screenshots/22_thrumbo_*.png (does NOT touch the other gallery files).
# Usage: tools/showcase/run_thrumbo_shots.sh [label] [Language]   e.g. GAME_LANG="German (Deutsch)" SUFFIX=_de
set -u
REPO="$(cd "$(dirname "$0")/../.." && pwd)"; MODDIR=${MODDIR:-$REPO}
LABEL=${1:-thrumbo1}; SUFFIX=${SUFFIX:-}
MODDIR=$MODDIR TIMEOUT=${TIMEOUT:-600} "$REPO/tools/run_test.sh" showcase.thrumbo $LABEL
code=$?
SRC=/private/tmp/claude-501/hh_tests/thrumbo_shots
mkdir -p "$REPO/docs/screenshots"
for f in "$SRC"/22_*.png; do [ -f "$f" ] && sips -Z 1600 "$f" --out "$REPO/docs/screenshots/$(basename "$f" .png)$SUFFIX.png" >/dev/null; done
ls -la "$REPO"/docs/screenshots/22_*
exit $code
