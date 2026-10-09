#!/usr/bin/env bash
# The task-suite runner (T-17): runs every task under eval/tasks through
# `lunate -p --json --yolo` in a fresh copy of its fixture repo, checks the
# result with the task's check.sh, and appends one row per suite run to
# eval/results.csv. See eval/README.md for the task format and row schema.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

TASKS_DIR="${ROOT}/eval/tasks"
RESULTS_FILE="${LUNATE_EVAL_RESULTS:-${ROOT}/eval/results.csv}"
DEFAULT_LUNATE_BIN="${ROOT}/src/Lunate.Coding/bin/Release/net10.0/lunate.dll"

MODEL=""
FILTER=""
PHASE="3"
LUNATE_BIN="${DEFAULT_LUNATE_BIN}"
KEEP=0

usage() {
  cat <<'EOF'
Usage: scripts/eval.sh --model <id> [--filter <task>] [--phase <n>] [--lunate-bin <path>] [--keep]

  --model <id>        the exact model id pinned in the results row (LUNATE_MODEL for the run)
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

if [ -z "$MODEL" ]; then
  echo "eval: --model <id> is required; the exact model id is pinned in the results row" >&2
  exit 2
fi

case "$PHASE" in
  ''|*[!0-9]*)
    echo "eval: --phase must be a non-negative integer, but was '${PHASE}'" >&2
    exit 2
    ;;
esac

if [ ! -e "$LUNATE_BIN" ]; then
  echo "eval: lunate build '${LUNATE_BIN}' not found." >&2
  echo "eval: build it with 'dotnet build src/Lunate.Coding -c Release' (or run scripts/verify.sh), or pass --lunate-bin <path>." >&2
  exit 1
fi

if ! command -v python3 >/dev/null 2>&1; then
  echo "eval: python3 is required to parse the JSONL stream and write the results row" >&2
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

echo "eval: model=${MODEL} phase=${PHASE} tasks=${#TASKS[@]}"
for name in "${TASKS[@]}"; do
  task_dir="${TASKS_DIR}/${name}"
  work="${WORK_ROOT}/${name}"
  mkdir -p "$work"
  cp -R "${task_dir}/repo/." "$work/"

  echo "==> ${name}"
  start_ns="$(python3 -c 'import time; print(time.monotonic_ns())')"
  set +e
  (
    cd "$work" &&
      LUNATE_MODEL="$MODEL" "${LUNATE_CMD[@]}" -p --json --yolo "$(cat "${task_dir}/task.md")" \
        >"${WORK_ROOT}/${name}.events.jsonl" 2>"${WORK_ROOT}/${name}.run.log"
  )
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
    python3 - "${WORK_ROOT}/${name}.events.jsonl" <<'PY'
import json
import sys

steps = 0
tokens = 0
tiers = set()
tool_names = {}
try:
    lines = [json.loads(line) for line in open(sys.argv[1], encoding="utf-8") if line.strip()]
except FileNotFoundError:
    lines = []

for line in lines:
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

print(f"{steps}\t{tokens}\t{';'.join(sorted(tiers)) if tiers else 'none'}")
PY
  )"
  IFS=$'\t' read -r steps tokens edit_tiers <<<"$metrics"

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

  printf '%s\t%s\t%s\t%s\t%s\t%s\t%s\n' \
    "$name" "$passed" "$run_exit" "$steps" "$tokens" "$seconds" "$edit_tiers" >>"$SUMMARY"
done

python3 - "$SUMMARY" "$RESULTS_FILE" "$MODEL" "$PHASE" <<'PY'
import csv
import datetime
import sys

summary_path, results_path, model, phase = sys.argv[1:5]
rows = []
with open(summary_path, encoding="utf-8") as summary:
    for line in summary:
        line = line.rstrip("\n")
        if not line:
            continue
        name, passed, run_exit, steps, tokens, seconds, edit_tiers = line.split("\t")
        rows.append(
            {
                "name": name,
                "passed": passed == "1",
                "run_exit": run_exit,
                "steps": int(steps),
                "tokens": int(tokens),
                "seconds": float(seconds),
                "tiers": edit_tiers,
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

row = [
    datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%d"),
    phase,
    "lunate",
    model,
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
