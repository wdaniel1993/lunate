#!/usr/bin/env bash
# S-1 evidence runner. Throwaway; not part of the repo gates.
#
# Builds both spike agents in Release, then runs the six checks and writes raw
# output to evidence/. Usage: docs/spikes/S-1/run-checks.sh
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")"

DOTNET="${DOTNET:-dotnet}"
EVIDENCE="evidence"
mkdir -p "$EVIDENCE"

OWN="OwnLoop/bin/Release/net10.0/OwnLoop"
MAF="MafHarness/bin/Release/net10.0/MafHarness"

echo "== build (Release) =="
"$DOTNET" build Shared/Spike.Shared.csproj -c Release --nologo >/dev/null
"$DOTNET" build OwnLoop/OwnLoop.csproj -c Release --nologo >/dev/null
"$DOTNET" build MafHarness/MafHarness.csproj -c Release --nologo >/dev/null

run_probe() {
  local binary="$1" out="$EVIDENCE/$2"
  : > "$out"
  for i in 1 2 3 4 5; do
    local log
    log="$(mktemp)"
    "$binary" probe >"$log" 2>&1 &
    local pid=$!
    for _ in $(seq 1 300); do
      if grep -q '^READY' "$log" 2>/dev/null; then break; fi
      sleep 0.1
    done
    sleep 0.5
    local rss
    rss="$(ps -o rss= -p "$pid" 2>/dev/null | tr -d ' ' || echo unknown)"
    kill "$pid" 2>/dev/null || true
    wait "$pid" 2>/dev/null || true
    {
      echo "run=$i"
      grep -E 'agent_build_ms|startup_ms|first_run_ms|managed_heap_bytes' "$log" || true
      echo "rss_kb=${rss:-unknown}"
    } >>"$out"
    rm -f "$log"
  done
}

echo "== check 1: startup and idle memory =="
run_probe "$OWN" check1-own-loop.txt
run_probe "$MAF" check1-maf-harness.txt
python3 - "$EVIDENCE/check1-own-loop.txt" "$EVIDENCE/check1-maf-harness.txt" <<'PY' | tee "$EVIDENCE/check1-summary.txt"
import re, statistics, sys

def parse(path):
    fields = {"startup_ms": [], "first_run_ms": [], "rss_kb": [], "managed_heap_bytes": []}
    for line in open(path):
        for key in fields:
            m = re.match(rf"{key}=([\d.]+)$", line.strip())
            if m:
                fields[key].append(float(m.group(1)))
    return fields

for path in sys.argv[1:]:
    f = parse(path)
    name = path.split("check1-")[1].replace(".txt", "")
    med = lambda xs: round(statistics.median(xs), 1) if xs else "n/a"
    print(f"{name}: startup_ms median={med(f['startup_ms'])} runs={f['startup_ms']}")
    print(f"{name}: first_run_ms median={med(f['first_run_ms'])}")
    print(f"{name}: rss_kb median={med(f['rss_kb'])}")
    print(f"{name}: managed_heap_bytes median={med(f['managed_heap_bytes'])}")
PY

echo "== check 2: prompt size =="
"$OWN" check2 >"$EVIDENCE/check2-own-loop.txt"
"$MAF" check2 >"$EVIDENCE/check2-maf-harness.txt"

echo "== check 3: event stream (canonical, steering, cancel) =="
"$OWN" check3 >"$EVIDENCE/check3-own-loop.txt"
"$MAF" check3 >"$EVIDENCE/check3-maf-harness.txt"

echo "== check 4: approval hooks =="
"$OWN" check4 >"$EVIDENCE/check4-own-loop.txt"
"$MAF" check4 >"$EVIDENCE/check4-maf-harness.txt"

echo "== check 5: recording and deterministic replay =="
"$OWN" check5 >"$EVIDENCE/check5-own-loop.txt"
"$MAF" check5 >"$EVIDENCE/check5-maf-harness.txt"

echo "== split-argument robustness =="
"$OWN" split >"$EVIDENCE/split-own-loop.txt"
"$MAF" split >"$EVIDENCE/split-maf-harness.txt"

echo "== error-path probe (revision item 2) =="
"$MAF" errorpath >"$EVIDENCE/errorpath-maf-harness.txt"

echo "evidence written to $EVIDENCE/"
