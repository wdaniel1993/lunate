#!/usr/bin/env bash
# S-5: lines of code per variant (excluding tests, bin, obj) plus shared harness.
# Usage: bash docs/spikes/S-5/loc.sh
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
cd "$ROOT"
S5=docs/spikes/S-5
EVIDENCE=$S5/evidence
mkdir -p "$EVIDENCE"

{
  for directory in S5.Baseline S5.VariantA S5.VariantB S5.VariantBPlus S5.Harness S5.Tests; do
    echo "== $directory"
    find "$S5/$directory" -name '*.cs' -not -path '*/obj/*' -not -path '*/bin/*' -print0 \
      | sort -z \
      | xargs -0 wc -l
  done
} | tee "$EVIDENCE/loc.txt"
