#!/usr/bin/env bash
# S-5: replay the identical scenario on every variant and prove the transcripts match.
# Usage: bash docs/spikes/S-5/run-scenario.sh
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
cd "$ROOT"
S5=docs/spikes/S-5
EVIDENCE=$S5/evidence
export MSBUILDDISABLENODEREUSE=1

mkdir -p "$EVIDENCE"

for variant in Baseline VariantA VariantB VariantBPlus; do
  dotnet build "$S5/S5.$variant/S5.$variant.csproj" -c Release --nologo -v q
  dotnet "$S5/S5.$variant/bin/Release/net10.0/S5.$variant.dll" --scenario \
    > "$EVIDENCE/scenario-$variant.txt"
done

(cd "$EVIDENCE" && shasum scenario-Baseline.txt scenario-VariantA.txt scenario-VariantB.txt scenario-VariantBPlus.txt) \
  | tee "$EVIDENCE/scenario-sha1.txt"

count="$(awk '{print $1}' "$EVIDENCE/scenario-sha1.txt" | sort -u | wc -l | tr -d ' ')"
if [ "$count" != "1" ]; then
  echo "run-scenario: transcripts differ across variants" >&2
  exit 1
fi

echo "run-scenario: all four transcripts identical"
