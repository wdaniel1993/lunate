#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
. "${SCRIPT_DIR}/lib.sh"

cd "${SCRIPT_DIR}/.."

BUDGET_MS="${BUDGET_MS:-150}"
CONFIGURATION="${CONFIGURATION:-Release}"
PUBLISH_DIR="artifacts/publish"

RID="${RID:-$(detect_rid verify)}"
BINARY="${PUBLISH_DIR}/${RID}/lunate"
if [ "${RID#win}" != "$RID" ]; then
  BINARY="${BINARY}.exe"
fi

step() {
  printf '\n==> %s\n' "$1"
}

step "build"
dotnet build lunate.sln -c "$CONFIGURATION" --nologo

step "test"
dotnet test --solution lunate.sln -c "$CONFIGURATION"

step "test (de-AT culture)"
# Non-English culture pass (S-5 finding). Effective on macOS/Linux; Windows
# runners keep the OS culture (LANG is not honored there).
LANG=de_AT.UTF-8 LC_ALL=de_AT.UTF-8 dotnet test --solution lunate.sln -c "$CONFIGURATION" --no-build

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
dotnet format lunate.sln --verify-no-changes --no-restore

step "public API"
if ! git diff --exit-code -- '*PublicAPI.Shipped.txt'; then
  echo "verify: PublicAPI.Shipped.txt changed" >&2
  exit 1
fi

echo "verify: OK"
