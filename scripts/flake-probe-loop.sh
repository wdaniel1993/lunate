#!/usr/bin/env bash
# Diagnostic loop for the ProcessRunner line-handler flake (macOS CI only).
# Round 2: the sightings happened MID-SUITE, so this loops the full test suite (suite context:
# warm host, prior process churn) and, cheaply, a few isolated probe runs. Failures are recorded
# in the log + copied into flake-artifacts/ (uploaded by the workflow).
set -uo pipefail

shard="${1:-0}"
BIN=tests/Lunate.Coding.Tests/bin/Release/net10.0/Lunate.Coding.Tests
ART="$PWD/flake-artifacts"
mkdir -p "$ART"
export FLK_ARTIFACT_DIR="$ART"
LOG="flake-probe-$shard.log"
: > "$LOG"
{
  echo "shard=$shard node=$(command -v node) $(node --version 2>/dev/null)"
  echo "start $(date -u +%FT%TZ)"
} >> "$LOG"

fails=0
runs=0

run_isolated() { # $1 = fully-qualified test, $2 = label
  local out
  runs=$((runs + 1))
  out=$("$BIN" -noLogo -method "$1" 2>&1)
  if ! grep -q "Failed: 0" <<<"$out"; then
    fails=$((fails + 1))
    {
      echo "==== FAIL [$2 isolated] run $runs $(date -u +%FT%TZ) ===="
      echo "$out" | tail -100
    } >> "$LOG"
  fi
}

# Cheap isolated warm-up: the instrumented probe only.
for _ in $(seq 1 "${PROBE_ITERS:-50}"); do
  run_isolated "Lunate.Coding.Tests.ProcessRunnerFlakeProbe.The_probe_reproduces_the_line_handler_round_trip" probe
done

# Suite context: the full Coding suite, repeated. Both the original test and the instrumented
# probe run inside every suite invocation.
for i in $(seq 1 "${SUITE_RUNS:-4}"); do
  out=$("$BIN" -noLogo 2>&1)
  runs=$((runs + 1))
  if ! grep -qE "Failed: 0" <<<"$out"; then
    fails=$((fails + 1))
    {
      echo "==== FAIL [suite] iter $i $(date -u +%FT%TZ) ===="
      grep -E "failed |Failed:" <<<"$out" | head -20
      echo "---"
      echo "$out" | tail -120
    } >> "$LOG"
  fi
  echo "suite $i done $(date -u +%FT%TZ)" >> "$LOG"
done

{
  echo "runs=$runs fails=$fails"
  echo "end $(date -u +%FT%TZ)"
} >> "$LOG"
tail -40 "$LOG"
