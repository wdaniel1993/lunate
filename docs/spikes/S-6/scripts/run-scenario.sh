#!/usr/bin/env bash
# S-6: replay the S-5 scenario on the XenoAtom harness three times, dump the
# raw ANSI stream of one run, and compare normalised transcripts.
# Usage: bash docs/spikes/S-6/scripts/run-scenario.sh
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../../.." && pwd)"
cd "$ROOT"
S6=docs/spikes/S-6
EVIDENCE=$S6/evidence
export MSBUILDDISABLENODEREUSE=1

dotnet build "$S6/S6.Harness/S6.Harness.csproj" -c Release --nologo -v q
mkdir -p "$EVIDENCE"

for run in 1 2 3; do
  S6_RAW_OUT="$EVIDENCE/scenario-run-$run.ansi" \
    dotnet "$S6/S6.Harness/bin/Release/net10.0/S6.Harness.dll" --scenario \
    > "$EVIDENCE/scenario-run-$run.txt" 2>&1 || true
  # ticks vary with wall-clock scheduling; strip them for the identity check.
  grep -v '^  key=' "$EVIDENCE/scenario-run-$run.txt" \
    | sed -E 's/ ticks=[0-9]+//' > "$EVIDENCE/scenario-run-$run.normalized.txt"
done

(cd "$EVIDENCE" && shasum scenario-run-*.normalized.txt) | tee "$EVIDENCE/scenario-sha1.txt"
count="$(awk '{print $1}' "$EVIDENCE/scenario-sha1.txt" | sort -u | wc -l | tr -d ' ')"
if [ "$count" != "1" ]; then
  echo "run-scenario: normalised transcripts differ" >&2
  exit 1
fi
echo "run-scenario: all three normalised transcripts identical"
