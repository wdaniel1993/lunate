#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
. "${SCRIPT_DIR}/lib.sh"

cd "${SCRIPT_DIR}/.."

RID="${RID:-$(detect_rid gate-tests)}"
BINARY="artifacts/publish/${RID}/lunate"
if [ "${RID#win}" != "$RID" ]; then
  BINARY="${BINARY}.exe"
fi

if [ ! -x "$BINARY" ]; then
  echo "gate-tests: published binary not found or not executable: $BINARY" >&2
  echo "gate-tests: run scripts/verify.sh first (or dotnet publish for RID=${RID})" >&2
  exit 1
fi

if ! dotnet csharpier --version >/dev/null 2>&1; then
  echo "gate-tests: csharpier not found; install it with: dotnet tool restore" >&2
  exit 1
fi

echo "gate-tests: perf.sh must fail with BUDGET_MS=1"
if BUDGET_MS=1 PERF_RUNS=5 PERF_WARMUP=1 PERF_RESULTS=artifacts/perf-gate-breach.json \
  "${SCRIPT_DIR}/perf.sh" "$BINARY" >/dev/null 2>&1; then
  echo "gate-tests: FAIL: perf.sh passed with BUDGET_MS=1; budget breach was not detected" >&2
  exit 1
fi

echo "gate-tests: perf.sh must pass with BUDGET_MS=999999"
if ! BUDGET_MS=999999 PERF_RUNS=5 PERF_WARMUP=1 PERF_RESULTS=artifacts/perf-gate-pass.json \
  "${SCRIPT_DIR}/perf.sh" "$BINARY" >/dev/null; then
  echo "gate-tests: FAIL: perf.sh failed with BUDGET_MS=999999" >&2
  exit 1
fi

FORMAT_DIR=""
SHIPPED_DIR=""
cleanup() {
  if [ -n "$FORMAT_DIR" ]; then
    rm -rf "$FORMAT_DIR"
  fi
  if [ -n "$SHIPPED_DIR" ]; then
    rm -rf "$SHIPPED_DIR"
  fi
}
trap cleanup EXIT

echo "gate-tests: formatting gate must fail on an unformatted file"
FORMAT_DIR="$(mktemp -d "${SCRIPT_DIR}/../.gate-format.XXXXXX")"
printf 'class C { void M() { var x = 1; } }\n' >"${FORMAT_DIR}/Unformatted.cs"
if dotnet csharpier check "$FORMAT_DIR" >/dev/null 2>&1; then
  echo "gate-tests: FAIL: csharpier check passed for an unformatted file" >&2
  exit 1
fi

echo "gate-tests: formatting gate must pass on a formatted file"
rm "${FORMAT_DIR}/Unformatted.cs"
printf 'class C\n{\n    void M()\n    {\n        var x = 1;\n    }\n}\n' >"${FORMAT_DIR}/Formatted.cs"
if ! dotnet csharpier check "$FORMAT_DIR" >/dev/null; then
  echo "gate-tests: FAIL: csharpier check failed for a formatted file" >&2
  exit 1
fi

echo "gate-tests: shipped-API gate must fail for a committed Shipped change on a branch"
SHIPPED_DIR="$(mktemp -d "${SCRIPT_DIR}/../.gate-shipped.XXXXXX")"
git -C "$SHIPPED_DIR" init -q
git -C "$SHIPPED_DIR" branch -m main
git -C "$SHIPPED_DIR" config user.name "gate-tests"
git -C "$SHIPPED_DIR" config user.email "gate-tests@example.invalid"
mkdir -p "${SHIPPED_DIR}/src/P"
printf '#nullable enable\n' >"${SHIPPED_DIR}/src/P/PublicAPI.Shipped.txt"
git -C "$SHIPPED_DIR" add -A
git -C "$SHIPPED_DIR" commit -qm "base"
git -C "$SHIPPED_DIR" checkout -qb change
printf 'P.Type\n' >>"${SHIPPED_DIR}/src/P/PublicAPI.Shipped.txt"
git -C "$SHIPPED_DIR" add -A
git -C "$SHIPPED_DIR" commit -qm "change"
if (cd "$SHIPPED_DIR" && . "${SCRIPT_DIR}/lib.sh" && check_shipped_api_unchanged) >/dev/null 2>&1; then
  echo "gate-tests: FAIL: shipped-API gate passed for a committed Shipped change on a branch" >&2
  exit 1
fi

echo "gate-tests: shipped-API gate must fail for an uncommitted Shipped change"
printf 'P.Other\n' >>"${SHIPPED_DIR}/src/P/PublicAPI.Shipped.txt"
if (cd "$SHIPPED_DIR" && . "${SCRIPT_DIR}/lib.sh" && check_shipped_api_unchanged) >/dev/null 2>&1; then
  echo "gate-tests: FAIL: shipped-API gate passed for an uncommitted Shipped change" >&2
  exit 1
fi

echo "gate-tests: shipped-API gate must fail for a deleted Shipped file"
git -C "$SHIPPED_DIR" checkout -- src/P/PublicAPI.Shipped.txt
git -C "$SHIPPED_DIR" rm -q src/P/PublicAPI.Shipped.txt
git -C "$SHIPPED_DIR" commit -qm "delete shipped"
if (cd "$SHIPPED_DIR" && . "${SCRIPT_DIR}/lib.sh" && check_shipped_api_unchanged) >/dev/null 2>&1; then
  echo "gate-tests: FAIL: shipped-API gate passed for a deleted Shipped file" >&2
  exit 1
fi

echo "gate-tests: shipped-API gate must pass for a newly added tracking file"
git -C "$SHIPPED_DIR" checkout -q main
git -C "$SHIPPED_DIR" checkout -qb bootstrap
mkdir -p "${SHIPPED_DIR}/src/P2"
printf '#nullable enable\n' >"${SHIPPED_DIR}/src/P2/PublicAPI.Shipped.txt"
git -C "$SHIPPED_DIR" add -A
git -C "$SHIPPED_DIR" commit -qm bootstrap
if ! (cd "$SHIPPED_DIR" && . "${SCRIPT_DIR}/lib.sh" && check_shipped_api_unchanged) >/dev/null; then
  echo "gate-tests: FAIL: shipped-API gate failed for a newly added tracking file" >&2
  exit 1
fi

echo "gate-tests: shipped-API gate must pass on a clean main tree"
git -C "$SHIPPED_DIR" checkout -q main
if ! (cd "$SHIPPED_DIR" && . "${SCRIPT_DIR}/lib.sh" && check_shipped_api_unchanged) >/dev/null; then
  echo "gate-tests: FAIL: shipped-API gate failed on a clean main tree" >&2
  exit 1
fi

echo "gate-tests: OK"
