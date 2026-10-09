#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
. "${SCRIPT_DIR}/lib.sh"

cd "${SCRIPT_DIR}/.."

BUDGET_MS="${BUDGET_MS:-150}"
CONFIGURATION="${CONFIGURATION:-Release}"
PUBLISH_DIR="artifacts/publish"

# English-first gate output: pin the dotnet CLI / test-runner UI language so
# localized toolchain text never leaks into logs on non-English machines. Only
# UI strings are pinned — the de-AT pass below still sets the TEST culture via
# LANG, and the product itself must behave identically under any machine culture.
export DOTNET_CLI_UI_LANGUAGE="${DOTNET_CLI_UI_LANGUAGE:-en}"

RID="${RID:-$(detect_rid verify)}"
BINARY="${PUBLISH_DIR}/${RID}/lunate"
if [ "${RID#win}" != "$RID" ]; then
  BINARY="${BINARY}.exe"
fi

step() {
  printf '\n==> %s\n' "$1"
}

# Runs a test pass and fails when it executed zero tests: a discovery
# regression (runner or argument drift) must never look green in CI. The MTP
# summary line is "total: N"; a missing or zero count is a failure. PIPESTATUS
# preserves the test runner's exit code through the tee.
run_tests() {
  local label="$1"
  shift
  local output status total
  output="$(mktemp)"
  set +e
  "$@" 2>&1 | tee "$output"
  status="${PIPESTATUS[0]}"
  set -e
  total="$(grep -oE 'total: [0-9]+' "$output" | tail -n 1 | grep -oE '[0-9]+' || true)"
  rm -f "$output"
  if [ "$status" -ne 0 ]; then
    echo "verify: ${label} failed (exit ${status})" >&2
    exit 1
  fi
  if [ -z "$total" ] || [ "$total" -eq 0 ]; then
    echo "verify: ${label} executed zero tests — test discovery regression?" >&2
    exit 1
  fi
}

step "tools"
# CSharpier is pinned in .config/dotnet-tools.json; restore makes it available locally.
if ! dotnet tool restore; then
  echo "verify: dotnet tool restore failed; run it from the repository root to install CSharpier (pinned in .config/dotnet-tools.json)" >&2
  exit 1
fi
if ! dotnet csharpier --version >/dev/null 2>&1; then
  echo "verify: csharpier not found; install it with: dotnet tool restore" >&2
  exit 1
fi

step "build"
dotnet build lunate.sln -c "$CONFIGURATION" --nologo

step "provider runtime assets"
# Providers are compile-private to Lunate.Ai, but their runtime assets must reach
# the app output or provider construction fails at run time.
for assembly in Anthropic OpenAI Microsoft.Extensions.AI; do
  if [ ! -f "src/Lunate.Coding/bin/${CONFIGURATION}/net10.0/${assembly}.dll" ]; then
    echo "verify: ${assembly}.dll is missing from the Lunate.Coding build output" >&2
    exit 1
  fi
done

step "test"
run_tests "test" dotnet test --solution lunate.sln -c "$CONFIGURATION"

step "test (de-AT culture)"
# Non-English culture pass (S-5 finding). Effective on macOS/Linux; Windows
# runners keep the OS culture (LANG is not honored there).
LANG=de_AT.UTF-8 LC_ALL=de_AT.UTF-8 run_tests "test (de-AT culture)" dotnet test --solution lunate.sln -c "$CONFIGURATION" --no-build

step "publish (${RID})"
dotnet publish src/Lunate.Coding/Lunate.Coding.csproj \
  -c "$CONFIGURATION" \
  -r "$RID" \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:PublishReadyToRun=true \
  -o "${PUBLISH_DIR}/${RID}" \
  --nologo

step "startup budget"
BUDGET_MS="$BUDGET_MS" "${SCRIPT_DIR}/perf.sh" "$BINARY"

step "format"
# CSharpier owns formatting; dotnet format keeps style and analyzer duties.
dotnet csharpier check .
dotnet format style lunate.sln --verify-no-changes --no-restore
dotnet format analyzers lunate.sln --verify-no-changes --no-restore

step "docs lint"
if ! command -v node >/dev/null 2>&1; then
  echo "verify: node is required for the documentation lint step; install Node.js (npx runs the pinned markdownlint-cli2)" >&2
  exit 1
fi
npx --yes markdownlint-cli2@0.23.3

step "public API"
if ! check_shipped_api_unchanged; then
  exit 1
fi

echo "verify: OK"
