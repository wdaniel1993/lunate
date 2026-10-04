#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")/.."

BUDGET_MS="${BUDGET_MS:-150}"
CONFIGURATION="${CONFIGURATION:-Release}"
PUBLISH_DIR="artifacts/publish"

detect_rid() {
  local os arch

  case "$(uname -s)" in
    Darwin) os="osx" ;;
    Linux) os="linux" ;;
    MINGW*|MSYS*|CYGWIN*) os="win" ;;
    *) echo "verify: unsupported OS: $(uname -s)" >&2; exit 1 ;;
  esac

  case "$(uname -m)" in
    arm64|aarch64) arch="arm64" ;;
    x86_64|amd64) arch="x64" ;;
    *) echo "verify: unsupported architecture: $(uname -m)" >&2; exit 1 ;;
  esac

  echo "${os}-${arch}"
}

RID="${RID:-$(detect_rid)}"
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

step "publish (${RID})"
dotnet publish src/Lunate.Coding/Lunate.Coding.csproj \
  -c "$CONFIGURATION" \
  -r "$RID" \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:PublishReadyToRun=true \
  -p:EnableCompressionInSingleFile=true \
  -o "${PUBLISH_DIR}/${RID}" \
  --nologo

step "startup budget"
BUDGET_MS="$BUDGET_MS" "$(dirname "$0")/perf.sh" "$BINARY"

step "format"
dotnet format lunate.sln --verify-no-changes --no-restore

step "public API"
if ! git diff --exit-code -- '*PublicAPI.Shipped.txt'; then
  echo "verify: PublicAPI.Shipped.txt changed" >&2
  exit 1
fi

echo "verify: OK"
