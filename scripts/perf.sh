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

if ! "$BINARY" --version >/dev/null 2>&1; then
  echo "perf: preflight failed: cannot run '$BINARY --version'" >&2
  exit 1
fi

mkdir -p "$(dirname "$RESULTS_FILE")"
RESULTS_ABS="$(cd "$(dirname "$RESULTS_FILE")" && pwd)/$(basename "$RESULTS_FILE")"
# hyperfine executes commands through the system shell (sh on Unix, cmd.exe on
# Windows). cmd.exe cannot run POSIX-style relative paths such as ./name, so on
# Windows convert to the absolute Windows path (backslash form) via cygpath.
BINARY_ABS="$(cd "$(dirname "$BINARY")" && pwd)/$(basename "$BINARY")"
case "$(uname -s)" in
  MINGW*|MSYS*|CYGWIN*)
    if command -v cygpath >/dev/null 2>&1; then
      HYPERFINE_CMD="$(cygpath -w "$BINARY_ABS") --version"
    else
      HYPERFINE_CMD="$BINARY_ABS --version"
    fi
    ;;
  *)
    HYPERFINE_CMD="$BINARY_ABS --version"
    ;;
esac

echo "perf: benchmarking: $HYPERFINE_CMD"
hyperfine --warmup "$WARMUP" --runs "$RUNS" --export-json "$RESULTS_ABS" "$HYPERFINE_CMD" >/dev/null

MEDIAN_MS="$(jq -r '.results[0].median * 1000 | round' "$RESULTS_ABS")"
printf 'startup median: %s ms (budget %s ms)\n' "$MEDIAN_MS" "$BUDGET_MS"

if [ "$MEDIAN_MS" -gt "$BUDGET_MS" ]; then
  echo "startup budget exceeded: ${MEDIAN_MS} ms > ${BUDGET_MS} ms" >&2
  exit 1
fi
