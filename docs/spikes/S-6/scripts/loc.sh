#!/usr/bin/env bash
# S-6: lines of code of the harness, tests and scripts (excluding bin/obj).
# Usage: bash docs/spikes/S-6/scripts/loc.sh
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../../.." && pwd)"
cd "$ROOT"
S6=docs/spikes/S-6
EVIDENCE=$S6/evidence
mkdir -p "$EVIDENCE"

{
  for directory in S6.Harness S6.Tests; do
    echo "== $directory"
    find "$S6/$directory" -name '*.cs' -not -path '*/obj/*' -not -path '*/bin/*' -print0 \
      | sort -z \
      | xargs -0 wc -l
  done
  echo "== scripts"
  find "$S6/scripts" -name '*.sh' -print0 | sort -z | xargs -0 wc -l
} | tee "$EVIDENCE/loc.txt"
