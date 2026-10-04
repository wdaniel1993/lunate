#!/usr/bin/env bash
# S-4 addendum helper: run a command and sample peak RSS of the whole process
# tree (the Roslyn build host is a child process, so a plain /usr/bin/time -l
# on the parent undercounts).
# Usage: bash docs/spikes/S-4/mem-sample.sh <label> <log> -- <cmd> [args...]
set -euo pipefail

label="$1"; log="$2"; shift 2
[ "$1" = "--" ] && shift

"$@" >"$log" 2>&1 &
pid=$!
max=0

tree() { echo "$1"; local c; for c in $(pgrep -P "$1" 2>/dev/null || true); do tree "$c"; done; }

while kill -0 "$pid" 2>/dev/null; do
  total=0
  for p in $(tree "$pid"); do
    rss=$(ps -o rss= -p "$p" 2>/dev/null | tr -d ' ' || true)
    if [ -n "$rss" ]; then total=$((total + rss)); fi
  done
  if [ "$total" -gt "$max" ]; then max=$total; fi
  sleep 0.15
done

wait "$pid"
code=$?
echo "label=$label exit=$code peak_rss_kb_total=$max"
grep -E '_ms=|errors=|workspace_failures' "$log" || true
exit "$code"
