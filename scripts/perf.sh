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
echo "perf: $(hyperfine --version)"

# The gate is the FLOOR: the fastest of the runs, printed beside the median for
# context (ADR-0005 addendum, 2026-10-25). Interference on a shared runner only
# ever adds time, so the fastest run estimates what the binary costs and the
# median estimates what the machine was doing; a budget taken on the median was
# reading the runner's thermal and scheduler state (identical code measured 81 to
# 364 ms on macOS legs). A real regression sits on the startup path and moves the
# floor with it, so nothing is hidden by the change.
measure_startup() {
  hyperfine --warmup "$WARMUP" --runs "$RUNS" --export-json "$RESULTS_ABS" "$HYPERFINE_CMD" >/dev/null
  # Two JSON shapes are in the wild: hyperfine 2.x writes the statistics under
  # .results[].summary.time_wall_clock, 1.x writes .min/.median at the top level.
  # Read whichever the installed version wrote — runner images and developer
  # machines drift (the macOS runner already carries 2.0.0 while a dev machine can
  # still have 1.20). A shape that fits neither is an error, never a silent pass.
  jq -r '
    .results[0] as $result
    | ($result | .min // .summary.time_wall_clock.min // (if .times then (.times | min) else null end)) as $floor
    | ($result | .median // .summary.time_wall_clock.median) as $median
    | if $floor == null or $median == null then
        error("perf: the hyperfine JSON carries no wall-clock timing")
      else
        "\($floor * 1000 | round) \($median * 1000 | round)"
      end
  ' "$RESULTS_ABS"
}

read -r FLOOR_MS MEDIAN_MS <<<"$(measure_startup || true)"
if ! [[ "${FLOOR_MS:-}" =~ ^[0-9]+$ && "${MEDIAN_MS:-}" =~ ^[0-9]+$ ]]; then
  echo "perf: could not read the startup measurement (floor='${FLOOR_MS:-}', median='${MEDIAN_MS:-}')" >&2
  exit 1
fi
printf 'startup: floor %s ms (median %s ms, budget %s ms)\n' \
  "$FLOOR_MS" "$MEDIAN_MS" "$BUDGET_MS"

# Noise policy (ADR-0005): a single budget miss re-runs the measurement once
# before failing, so a shared-runner noise burst does not redden the gate while
# a material regression still misses both attempts. Consecutive misses are a
# re-calibration (or regression) signal.
if [ "$FLOOR_MS" -gt "$BUDGET_MS" ]; then
  echo "startup floor ${FLOOR_MS} ms exceeds budget ${BUDGET_MS} ms; re-running once (noise policy)"
  read -r FLOOR_MS MEDIAN_MS <<<"$(measure_startup)"
  printf 'startup: floor %s ms (median %s ms, budget %s ms; retry)\n' \
    "$FLOOR_MS" "$MEDIAN_MS" "$BUDGET_MS"
fi

if [ "$FLOOR_MS" -gt "$BUDGET_MS" ]; then
  echo "startup budget exceeded: fastest of ${RUNS} runs ${FLOOR_MS} ms > ${BUDGET_MS} ms (median ${MEDIAN_MS} ms)" >&2
  exit 1
fi
