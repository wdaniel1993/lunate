#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
. "${SCRIPT_DIR}/lib.sh"

cd "${SCRIPT_DIR}/.."

BUDGET_MS="${BUDGET_MS:-150}"
RUNS="${PERF_RUNS:-20}"
WARMUP="${PERF_WARMUP:-3}"
RESULTS_FILE="${PERF_RESULTS:-artifacts/perf.json}"

BINARY="${1:-}"
if [ -z "$BINARY" ]; then
  RID="${RID:-$(detect_rid perf)}"
  BINARY="artifacts/publish/${RID}/lunate"
  if [ "${RID#win}" != "$RID" ]; then
    BINARY="${BINARY}.exe"
  fi
fi

if [ ! -x "$BINARY" ]; then
  echo "perf: binary not found or not executable: $BINARY" >&2
  exit 1
fi

mkdir -p "$(dirname "$RESULTS_FILE")"
hyperfine --warmup "$WARMUP" --runs "$RUNS" --export-json "$RESULTS_FILE" "\"$BINARY\" --version" >/dev/null

MEDIAN_MS="$(jq -r '.results[0].median * 1000 | round' "$RESULTS_FILE")"
printf 'startup median: %s ms (budget %s ms)\n' "$MEDIAN_MS" "$BUDGET_MS"

if [ "$MEDIAN_MS" -gt "$BUDGET_MS" ]; then
  echo "startup budget exceeded: ${MEDIAN_MS} ms > ${BUDGET_MS} ms" >&2
  exit 1
fi
