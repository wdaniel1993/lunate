#!/usr/bin/env bash
# Lunate memory budgets (ADR-0009): peak RSS sampled from outside the process,
# same method on all OSes; budgets are per OS because RSS accounting differs.
#
# Modes:
#   ci            selftest + idle report (CI); idle subject via MEMORY_SUBJECT
#                 (absent -> skip until T-18 provides the TUI)
#   selftest      proves the sampler and the gate logic
#   idle CMD...   measure CMD's peak RSS; CMD must stay alive (gate: MEMORY_GATE_MB)
#   workingset    TODO - enabled by T-22 (placeholder gate 250 MB)
#   roslyn        TODO - enabled by T-25 (gate 750 MB from the S-4 addendum)
#
# Env: MEMORY_GATE_MB (default 150), MEMORY_TARGET_MB (default 100),
#      MEMORY_SAMPLES_MIN (default 5; minimum ticks for a valid idle window).
set -euo pipefail

GATE_MB="${MEMORY_GATE_MB:-150}"
TARGET_MB="${MEMORY_TARGET_MB:-100}"
MIN_TICKS="${MEMORY_SAMPLES_MIN:-5}"

tree() { echo "$1"; local c; for c in $(pgrep -P "$1" 2>/dev/null || true); do tree "$c"; done; }

# RSS (KB) for a pid. macOS/Linux: `ps -o rss=`. Git Bash (MSYS): its ps has no
# `-o`, so map the MSYS pid to its WINPID and ask tasklist instead.
rss_kb() {
  local p="$1" out winpid
  out=$(ps -o rss= -p "$p" 2>/dev/null | tr -d ' ' || true)
  if [ -n "$out" ]; then echo "$out"; return 0; fi
  winpid=$(ps -p "$p" 2>/dev/null | awk 'NR==2 {print $4}' || true)
  [ -n "$winpid" ] || return 0
  out=$(tasklist //FI "PID eq $winpid" //FO CSV //NH 2>/dev/null | awk -F'","' '{print $5}' | tr -cd '0-9' || true)
  [ -n "$out" ] && echo "$out"
  return 0
}

# Runs CMD and samples peak RSS (KB, whole process tree) every 150 ms.
# Prints "<peak_kb> <ticks>".
sample_peak() {
  "$@" >/dev/null 2>&1 &
  local pid=$! peak=0 ticks=0 total rss
  while kill -0 "$pid" 2>/dev/null; do
    total=0
    for p in $(tree "$pid"); do
      rss=$(rss_kb "$p")
      if [ -n "$rss" ]; then total=$((total + rss)); fi
    done
    if [ "$total" -gt "$peak" ]; then peak=$total; fi
    ticks=$((ticks + 1))
    sleep 0.15
  done
  wait "$pid" 2>/dev/null || true
  echo "$peak $ticks"
}

kb_to_mb() { echo $(( ($1 + 512) / 1024 )); }

report() { # label peak_kb -> 0 ok, 1 over gate
  local label="$1" peak_kb="$2" peak_mb
  peak_mb=$(kb_to_mb "$peak_kb")
  if [ "$peak_mb" -gt "$GATE_MB" ]; then
    echo "memory $label: peak_rss_mb=$peak_mb gate=${GATE_MB}MB target=${TARGET_MB}MB verdict=over-gate"
    return 1
  fi
  echo "memory $label: peak_rss_mb=$peak_mb gate=${GATE_MB}MB target=${TARGET_MB}MB verdict=ok"
  return 0
}

mode_idle() { # [--report-only] CMD...
  local report_only=0
  if [ "${1:-}" = "--report-only" ]; then report_only=1; shift; fi
  [ "$#" -ge 1 ] || { echo "usage: memory.sh idle [--report-only] CMD [args...]" >&2; exit 2; }
  local out peak ticks
  out=$(sample_peak "$@")
  peak=${out%% *}; ticks=${out##* }
  if [ "$ticks" -lt "$MIN_TICKS" ]; then
    echo "memory idle: subject exited before an idle window could be sampled (ticks=$ticks)" >&2
    if [ "$report_only" = 1 ]; then echo "memory idle: skipped (no long-lived subject)"; exit 0; fi
    exit 1
  fi
  if report "$1" "$peak"; then exit 0; fi
  if [ "$report_only" = 1 ]; then exit 0; fi
  exit 1
}

mode_selftest() {
  local out peak ticks fast_ticks
  out=$(sample_peak sleep 2)
  peak=${out%% *}; ticks=${out##* }
  # Windows (Git Bash + tasklist) paces ~0.4-0.7 s per tick, so a 2 s window can
  # yield as few as 3 ticks on a loaded runner (observed: green=5, loaded=3).
  # Retry once with a long window before failing; a healthy sampler still passes
  # and a broken one fails both attempts (same noise policy as perf.sh, ADR-0005).
  if [ "$ticks" -lt "$MIN_TICKS" ]; then
    out=$(sample_peak sleep 10)
    peak=${out%% *}; ticks=${out##* }
  fi
  [ "$peak" -gt 0 ] || { echo "FAIL: sampler produced no reading" >&2; exit 1; }
  [ "$ticks" -ge "$MIN_TICKS" ] || { echo "FAIL: too few sampler ticks ($ticks)" >&2; exit 1; }
  if (GATE_MB=1; report x 500000) >/dev/null 2>&1; then
    echo "FAIL: gate logic did not trip on an over-gate reading" >&2; exit 1
  fi
  if ! (GATE_MB=100000; report x 50000) >/dev/null 2>&1; then
    echo "FAIL: gate logic rejected a clean reading" >&2; exit 1
  fi
  out=$(sample_peak true); fast_ticks=${out##* }
  if [ "$fast_ticks" -ge "$MIN_TICKS" ]; then
    echo "FAIL: fast-exit subject was accepted as an idle window" >&2; exit 1
  fi
  echo "memory selftest: OK (sampler peak_kb=$peak ticks=$ticks)"
}

mode_ci() {
  mode_selftest
  if [ -n "${MEMORY_SUBJECT:-}" ]; then
    # shellcheck disable=SC2086
    mode_idle --report-only $MEMORY_SUBJECT
  else
    echo "memory idle: no subject yet (the TUI lands in T-18); gate=${GATE_MB}MB target=${TARGET_MB}MB (ADR-0009, report-only)"
  fi
}

mode_todo() { # name card gate note
  echo "memory $1: not yet runnable - enabled by $2; planned gate $3 MB ($4)"
}

case "${1:-}" in
  ci) mode_ci ;;
  selftest) mode_selftest ;;
  idle) shift; mode_idle "$@" ;;
  workingset) mode_todo workingset T-22 250 "placeholder until calibrated" ;;
  roslyn) mode_todo roslyn T-25 750 "calibrated from the S-4 addendum" ;;
  *) sed -n '2,13p' "$0"; exit 2 ;;
esac
