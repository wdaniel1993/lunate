#!/usr/bin/env bash
# S-5 idle-memory sampler (S-4 method): run a command and sample peak RSS of
# its process tree every 150 ms.
# Usage: bash mem-sample.sh <label> <log> -- <cmd> [args...]
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
grep -E 'heap_bytes=|ready' "$log" || true
exit "$code"
