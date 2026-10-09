#!/usr/bin/env bash
# S-6: run the test suite RUNS times (default 50, S-5 method) and count flakes.
# xunit.v3 is executed through the test executable: `dotnet test` in this SDK
# discovers zero tests for MTP projects, while the dll runs them.
# Usage: RUNS=50 bash docs/spikes/S-6/scripts/run-flake.sh
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../../.." && pwd)"
cd "$ROOT"
S6=docs/spikes/S-6
EVIDENCE=$S6/evidence
RUNS="${RUNS:-50}"
export MSBUILDDISABLENODEREUSE=1

dotnet build "$S6/S6.Tests/S6.Tests.csproj" -c Release --nologo -v q
mkdir -p "$EVIDENCE/flake"
: > "$EVIDENCE/flake/exit-codes.txt"

for i in $(seq -w 1 "$RUNS"); do
  set +e
  dotnet "$S6/S6.Tests/bin/Release/net10.0/S6.Tests.dll" \
    > "$EVIDENCE/flake/run-$i.txt" 2>&1
  code=$?
  set -e
  echo "run-$i exit=$code" >> "$EVIDENCE/flake/exit-codes.txt"
done

python3 - "$RUNS" "$EVIDENCE" <<'PY' | tee "$EVIDENCE/flake-report.txt"
import glob, re, sys
runs, evidence = int(sys.argv[1]), sys.argv[2]
files = sorted(glob.glob(f'{evidence}/flake/run-*.txt'))
failed_suites = 0
failed_tests = 0
total_tests = 0
for path in files:
    text = open(path).read()
    failed_tests += len(re.findall(r'^\s+\S+ \[FAIL\]', text, re.M))
    m = re.search(r'Total: (\d+), Errors: (\d+), Failed: (\d+)', text)
    if m:
        total_tests += int(m.group(1))
    if 'Failed: 0' not in text or 'Errors: 0' not in text:
        failed_suites += 1
print(f'runs={len(files)}')
print(f'failed_suite_runs={failed_suites}')
print(f'total_failed_test_instances={failed_tests}')
print(f'total_test_executions={total_tests}')
PY
