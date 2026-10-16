#!/usr/bin/env bash
# Diagnostic loop for the ProcessRunner line-handler flake (macOS CI only).
# Runs the original test once (cold shot), then the instrumented probe repeatedly.
# Failures are recorded in the log + copied into flake-artifacts/ (uploaded by the workflow).
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
run_once() { # $1 = fully-qualified test, $2 = label
  local out
  runs=$((runs + 1))
  out=$("$BIN" -noLogo -method "$1" 2>&1)
  if ! grep -q "Failed: 0" <<<"$out"; then
    fails=$((fails + 1))
    {
      echo "==== FAIL [$2] run $runs $(date -u +%FT%TZ) ===="
      echo "$out" | tail -100
    } >> "$LOG"
  fi
}

run_once "Lunate.Coding.Tests.ProcessRunnerTests.The_line_handler_answers_every_stdout_line_in_order" original
for _ in $(seq 1 "${PROBE_ITERS:-250}"); do
  run_once "Lunate.Coding.Tests.ProcessRunnerFlakeProbe.The_probe_reproduces_the_line_handler_round_trip" probe
done

{
  echo "runs=$runs fails=$fails"
  echo "end $(date -u +%FT%TZ)"
} >> "$LOG"
tail -40 "$LOG"
