#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")/.."

BUDGET_MS="${BUDGET_MS:-150}"
RUNS="${PERF_RUNS:-20}"
WARMUP="${PERF_WARMUP:-3}"
RESULTS_FILE="${PERF_RESULTS:-artifacts/perf.json}"

detect_rid() {
  local os arch

  case "$(uname -s)" in
    Darwin) os="osx" ;;
    Linux) os="linux" ;;
    MINGW*|MSYS*|CYGWIN*) os="win" ;;
    *) echo "perf: unsupported OS: $(uname -s)" >&2; exit 1 ;;
  esac

  case "$(uname -m)" in
    arm64|aarch64) arch="arm64" ;;
    x86_64|amd64) arch="x64" ;;
    *) echo "perf: unsupported architecture: $(uname -m)" >&2; exit 1 ;;
  esac

  echo "${os}-${arch}"
}

BINARY="${1:-}"
if [ -z "$BINARY" ]; then
  RID="${RID:-$(detect_rid)}"
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
