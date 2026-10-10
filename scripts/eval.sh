#!/usr/bin/env bash
# The task-suite runner (T-17) and the head-to-head driver (T-35): runs every
# task under eval/tasks through the selected tool in a fresh copy of its
# fixture repo, checks the result with the task's check.sh, and appends one row
# per suite run to eval/results.csv. See eval/README.md for the task format,
# row schema and the per-tool metric mapping.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

TASKS_DIR="${ROOT}/eval/tasks"
RESULTS_FILE="${LUNATE_EVAL_RESULTS:-${ROOT}/eval/results.csv}"
DEFAULT_LUNATE_BIN="${ROOT}/src/Lunate.Coding/bin/Release/net10.0/lunate.dll"

MODEL=""
FILTER=""
PHASE="3"
TOOL="lunate"
LUNATE_BIN="${DEFAULT_LUNATE_BIN}"
KEEP=0

usage() {
  cat <<'EOF'
Usage: scripts/eval.sh --model <id> [--tool <lunate|claude|opencode>] [--filter <task>] [--phase <n>] [--lunate-bin <path>] [--keep]

  --model <id>        the model id for the run: exported as LUNATE_MODEL (lunate)
                      or passed as -m (opencode). For claude the model comes from
                      the run's own JSON output, so --model is not required.
  --tool <name>       which tool to run: lunate (default), claude or opencode.
  --filter <task>     run one task by directory name
  --phase <n>         the phase column value (default: 3)
  --lunate-bin <path> the lunate build to run; a .dll is invoked via dotnet
                      (default: src/Lunate.Coding/bin/Release/net10.0/lunate.dll)
  --keep              keep every task working copy, also on pass
EOF
}

while [ $# -gt 0 ]; do
  case "$1" in
    --model)
      MODEL="${2:?--model needs a value}"
      shift 2
      ;;
    --tool)
      TOOL="${2:?--tool needs a value}"
      shift 2
      ;;
    --filter)
      FILTER="${2:?--filter needs a value}"
      shift 2
      ;;
    --phase)
      PHASE="${2:?--phase needs a value}"
      shift 2
      ;;
    --lunate-bin)
      LUNATE_BIN="${2:?--lunate-bin needs a value}"
      shift 2
      ;;
    --keep)
      KEEP=1
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "eval: unknown option '$1'" >&2
      usage >&2
      exit 2
      ;;
  esac
done

case "$TOOL" in
  lunate|claude|opencode) ;;
  *)
    echo "eval: --tool must be one of lunate, claude, opencode, but was '${TOOL}'" >&2
    exit 2
    ;;
esac

if [ "$TOOL" != "claude" ] && [ -z "$MODEL" ]; then
  echo "eval: --model <id> is required for tool '${TOOL}'; the exact model id is pinned in the results row" >&2
  exit 2
fi

case "$PHASE" in
  ''|*[!0-9]*)
    echo "eval: --phase must be a non-negative integer, but was '${PHASE}'" >&2
    exit 2
    ;;
esac

if [ "$TOOL" = "lunate" ]; then
  if [ ! -e "$LUNATE_BIN" ]; then
    echo "eval: lunate build '${LUNATE_BIN}' not found." >&2
    echo "eval: build it with 'dotnet build src/Lunate.Coding -c Release' (or run scripts/verify.sh), or pass --lunate-bin <path>." >&2
    exit 1
  fi
else
  if ! command -v "$TOOL" >/dev/null 2>&1; then
    echo "eval: '${TOOL}' is not on PATH." >&2
    exit 1
  fi
  if [ "$TOOL" = "claude" ] && ! command -v csharp-ls >/dev/null 2>&1; then
    echo "eval: warning: csharp-ls is not on PATH; the C# LSP plugin will not start for claude runs" >&2
  fi
fi

if ! command -v python3 >/dev/null 2>&1; then
  echo "eval: python3 is required to parse the tool output and write the results row" >&2
  exit 1
fi

LUNATE_CMD=()
case "$LUNATE_BIN" in
  *.dll) LUNATE_CMD=(dotnet "$LUNATE_BIN") ;;
  *) LUNATE_CMD=("$LUNATE_BIN") ;;
esac

TASKS=()
for dir in "${TASKS_DIR}"/*/; do
  [ -d "$dir" ] || continue
  name="$(basename "$dir")"
  [ -f "${dir}task.md" ] || continue
  if [ ! -f "${dir}check.sh" ]; then
    echo "eval: task '${name}' has no check.sh" >&2
    exit 1
  fi
  if [ ! -d "${dir}repo" ]; then
    echo "eval: task '${name}' has no repo/" >&2
    exit 1
  fi
  if [ -n "$FILTER" ] && [ "$name" != "$FILTER" ]; then
    continue
  fi
  TASKS+=("$name")
done

if [ ${#TASKS[@]} -eq 0 ]; then
  echo "eval: no task matched filter '${FILTER}' under ${TASKS_DIR}" >&2
  exit 1
fi

WORK_ROOT="$(mktemp -d "${TMPDIR:-/tmp}/lunate-eval.XXXXXX")"
SUMMARY="${WORK_ROOT}/summary.tsv"
: >"$SUMMARY"

cleanup() {
  if [ "$KEEP" -eq 0 ]; then
    for name in "${TASKS[@]}"; do
      if [ -f "${WORK_ROOT}/${name}.passed" ]; then
        rm -rf "${WORK_ROOT:?}/${name}"
      fi
    done
  fi
}
trap cleanup EXIT

echo "eval: tool=${TOOL} model=${MODEL:-<from output>} phase=${PHASE} tasks=${#TASKS[@]}"
for name in "${TASKS[@]}"; do
  task_dir="${TASKS_DIR}/${name}"
  work="${WORK_ROOT}/${name}"
  mkdir -p "$work"
  cp -R "${task_dir}/repo/." "$work/"

  echo "==> ${name}"
  start_ns="$(python3 -c 'import time; print(time.monotonic_ns())')"
  set +e
  case "$TOOL" in
    lunate)
      (
        cd "$work" &&
          LUNATE_MODEL="$MODEL" "${LUNATE_CMD[@]}" -p --json --yolo "$(cat "${task_dir}/task.md")" \
            >"${WORK_ROOT}/${name}.events.jsonl" 2>"${WORK_ROOT}/${name}.run.log"
      )
      ;;
    claude)
      (
        cd "$work" &&
          claude -p "$(cat "${task_dir}/task.md")" --output-format json --dangerously-skip-permissions \
            >"${WORK_ROOT}/${name}.events.jsonl" 2>"${WORK_ROOT}/${name}.run.log"
      )
      ;;
    opencode)
      (
        cd "$work" &&
          opencode run --format json --auto -m "$MODEL" "$(cat "${task_dir}/task.md")" \
            >"${WORK_ROOT}/${name}.events.jsonl" 2>"${WORK_ROOT}/${name}.run.log"
      )
      ;;
  esac
  run_exit=$?
  set -e
  end_ns="$(python3 -c 'import time; print(time.monotonic_ns())')"
  seconds="$(python3 -c "print(f'{(${end_ns} - ${start_ns}) / 1e9:.3f}')")"

  set +e
  (
    cd "$work" && bash "${task_dir}/check.sh" >"${WORK_ROOT}/${name}.check.log" 2>&1
  )
  check_exit=$?
  set -e

  metrics="$(
    python3 - "$TOOL" "${WORK_ROOT}/${name}.events.jsonl" <<'PY'
import json
import sys

tool, path = sys.argv[1:3]
steps = 0
tokens = 0
tiers = set()
model = ""


def read_lines():
    try:
        return [json.loads(line) for line in open(path, encoding="utf-8") if line.strip()]
    except (FileNotFoundError, json.JSONDecodeError):
        return []


if tool == "lunate":
    tool_names = {}
    for line in read_lines():
        kind = line.get("type")
        if kind == "tool_call_start":
            tool_names[line.get("callId")] = line.get("toolName")
        elif kind == "usage_updated":
            steps += 1
            usage = line.get("usage") or {}
            total = usage.get("totalTokenCount")
            if total is None:
                total = (usage.get("inputTokenCount") or 0) + (usage.get("outputTokenCount") or 0)
            tokens += total
        elif kind == "tool_call_result":
            details = line.get("details")
            if (
                isinstance(details, dict)
                and "matchTier" in details
                and tool_names.get(line.get("callId")) == "edit"
            ):
                tiers.add(details["matchTier"])
elif tool == "claude":
    lines = read_lines()
    payload = lines[0] if lines else {}
    steps = int(payload.get("num_turns") or 0)
    usage = payload.get("usage") or {}
    tokens = (
        (usage.get("input_tokens") or 0)
        + (usage.get("cache_creation_input_tokens") or 0)
        + (usage.get("cache_read_input_tokens") or 0)
        + (usage.get("output_tokens") or 0)
    )
    tiers.add("n/a")
    model_usage = payload.get("modelUsage") or {}
    if model_usage:
        model = next(iter(model_usage))
elif tool == "opencode":
    for line in read_lines():
        if line.get("type") == "step_finish":
            steps += 1
            part = line.get("part") or {}
            tokens += (part.get("tokens") or {}).get("total") or 0
    tiers.add("n/a")

print(f"{steps}\t{tokens}\t{';'.join(sorted(tiers)) if tiers else 'none'}\t{model}")
PY
  )"
  IFS=$'\t' read -r steps tokens edit_tiers model_used <<<"$metrics"

  if [ "$check_exit" -eq 0 ]; then
    passed=1
    touch "${WORK_ROOT}/${name}.passed"
    echo "    passed (steps=${steps} tokens=${tokens} seconds=${seconds} edit_tiers=${edit_tiers})"
  else
    passed=0
    echo "    FAILED (check exit ${check_exit}; run exit ${run_exit})" >&2
    echo "    work dir kept: ${work}" >&2
    echo "    check log: ${WORK_ROOT}/${name}.check.log" >&2
  fi

  printf '%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\n' \
    "$name" "$passed" "$run_exit" "$steps" "$tokens" "$seconds" "$edit_tiers" "$model_used" >>"$SUMMARY"
done

python3 - "$SUMMARY" "$RESULTS_FILE" "$MODEL" "$PHASE" "$TOOL" <<'PY'
import csv
import datetime
import sys

summary_path, results_path, model, phase, tool = sys.argv[1:6]
rows = []
with open(summary_path, encoding="utf-8") as summary:
    for line in summary:
        line = line.rstrip("\n")
        if not line:
            continue
        name, passed, run_exit, steps, tokens, seconds, edit_tiers, model_used = line.split("\t")
        rows.append(
            {
                "name": name,
                "passed": passed == "1",
                "run_exit": run_exit,
                "steps": int(steps),
                "tokens": int(tokens),
                "seconds": float(seconds),
                "tiers": edit_tiers,
                "model_used": model_used,
            }
        )

tasks = len(rows)
passed = sum(1 for row in rows if row["passed"])
steps = sum(row["steps"] for row in rows)
tokens = sum(row["tokens"] for row in rows)
seconds = sum(row["seconds"] for row in rows)
tiers = sorted({tier for row in rows for tier in row["tiers"].split(";") if tier != "none"})
failures = [
    f"{row['name']} (check failed, run exit {row['run_exit']})"
    for row in rows
    if not row["passed"]
]
run_models = sorted({row["model_used"] for row in rows if row["model_used"]})

row = [
    datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%d"),
    phase,
    tool,
    run_models[0] if run_models else model,
    tasks,
    passed,
    f"{passed / tasks:.3f}",
    steps,
    tokens,
    f"{seconds:.3f}",
    ";".join(tiers) if tiers else "none",
    "failed: " + ", ".join(failures) if failures else "",
]

try:
    with open(results_path, encoding="utf-8") as existing:
        has_header = existing.read(1) != ""
except FileNotFoundError:
    has_header = False

with open(results_path, "a", encoding="utf-8", newline="") as results:
    writer = csv.writer(results, lineterminator="\n")
    if not has_header:
        writer.writerow(
            [
                "date",
                "phase",
                "tool",
                "model",
                "tasks",
                "passed",
                "pass_rate",
                "steps",
                "total_tokens",
                "seconds",
                "edit_tiers",
                "notes",
            ]
        )
    writer.writerow(row)

print(",".join(str(value) for value in row))
print(f"eval: {passed}/{tasks} passed; row appended to {results_path}")
PY
