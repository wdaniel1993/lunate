#!/usr/bin/env bash
# S-5: run the shared test list 50x (each run exercises all three variant
# suites) and report flakes per variant.
# Usage: bash docs/spikes/S-5/run-flake.sh
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
cd "$ROOT"
S5=docs/spikes/S-5
EVIDENCE=$S5/evidence
RUNS="${RUNS:-50}"
export MSBUILDDISABLENODEREUSE=1

dotnet build "$S5/S5.Tests/S5.Tests.csproj" -c Release --nologo -v q
mkdir -p "$EVIDENCE/flake"
: > "$EVIDENCE/flake/exit-codes.txt"

for i in $(seq -w 1 "$RUNS"); do
  set +e
  dotnet test --project "$S5/S5.Tests/S5.Tests.csproj" -c Release --no-build \
    > "$EVIDENCE/flake/run-$i.txt" 2>&1
  code=$?
  set -e
  echo "run-$i exit=$code" >> "$EVIDENCE/flake/exit-codes.txt"
done

python3 - "$RUNS" "$EVIDENCE" <<'PY' | tee "$EVIDENCE/flake-report.txt"
import glob, re, sys
runs, evidence = int(sys.argv[1]), sys.argv[2]
files = sorted(glob.glob(f'{evidence}/flake/run-*.txt'))
variants = ['VariantATests', 'VariantBTests', 'VariantBPlusTests']
counts = {v: 0 for v in variants}
failed_suites = 0
for path in files:
    text = open(path).read()
    for v in variants:
        counts[v] += len(re.findall(rf'^failed S5\.Tests\.{v}\.', text, re.M))
    if 'Test run summary: Failed!' in text:
        failed_suites += 1
print(f'runs={len(files)}')
print(f'failed_suite_runs={failed_suites}')
for v in variants:
    print(f'{v} failed_test_instances={counts[v]}')
print(f'total_failed_lines={sum(counts.values())}')
print(f'total_test_executions={len(files) * 48}')
PY
