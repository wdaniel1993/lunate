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

VERIFY_TMP_DIRS=()

verify_tmp() {
  local dir
  dir="$(mktemp -d)"
  VERIFY_TMP_DIRS+=("$dir")
  printf '%s\n' "$dir"
}

verify_cleanup() {
  if [ "${#VERIFY_TMP_DIRS[@]}" -gt 0 ]; then
    rm -rf "${VERIFY_TMP_DIRS[@]}"
  fi
}

trap verify_cleanup EXIT

# The release version, exactly as release.yml gates the tag against it.
product_version() {
  sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props
}

sha256_file() {
  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum "$1" | awk '{ print $1 }'
  else
    shasum -a 256 "$1" | awk '{ print $1 }'
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

step "install script (fixture release)"
version="$(product_version)"
if [ -z "$version" ]; then
  echo "verify: cannot read <Version> from Directory.Build.props" >&2
  exit 1
fi
install_tmp="$(verify_tmp)"

if [ "${RID#win}" != "$RID" ]; then
  # The Windows bash (Git Bash) must be refused: Windows installs via install.ps1.
  refusal_log="${install_tmp}/refusal.log"
  if sh install.sh >"$refusal_log" 2>&1; then
    echo "verify: install.sh must refuse Windows platforms (use install.ps1), but it exited 0" >&2
    exit 1
  fi
  if ! grep -q 'install.ps1' "$refusal_log"; then
    echo "verify: install.sh refusal does not point at install.ps1:" >&2
    cat "$refusal_log" >&2
    exit 1
  fi
else
  fixture="${install_tmp}/fixture"
  prefix="${install_tmp}/prefix"
  bad_fixture="${install_tmp}/bad-fixture"
  bad_prefix="${install_tmp}/bad-prefix"
  log="${install_tmp}/install.log"
  mkdir -p "$fixture" "$prefix" "$bad_fixture" "$bad_prefix"

  install_asset="lunate-${RID}.tar.gz"
  tar -czf "${fixture}/${install_asset}" -C "${PUBLISH_DIR}/${RID}" lunate
  printf '%s  %s\n' "$(sha256_file "${fixture}/${install_asset}")" "$install_asset" >"${fixture}/SHA256SUMS"

  if ! LUNATE_INSTALL_BASE_URL="file://${fixture}" LUNATE_INSTALL_PREFIX="$prefix" \
    sh install.sh --version "v${version}" >"$log" 2>&1; then
    echo "verify: install.sh failed against the fixture release:" >&2
    cat "$log" >&2
    exit 1
  fi
  installed="$("${prefix}/lunate" --version)"
  published="$("${BINARY}" --version)"
  if [ "$installed" != "$published" ]; then
    echo "verify: installed lunate reports ${installed}, expected ${published}" >&2
    exit 1
  fi

  cp -R "${fixture}/." "$bad_fixture/"
  printf 'corrupt\n' >>"${bad_fixture}/${install_asset}"
  if LUNATE_INSTALL_BASE_URL="file://${bad_fixture}" LUNATE_INSTALL_PREFIX="$bad_prefix" \
    sh install.sh >"$log" 2>&1; then
    echo "verify: install.sh accepted a checksum mismatch" >&2
    exit 1
  fi
  if [ -e "${bad_prefix}/lunate" ]; then
    echo "verify: install.sh left a partial install after a checksum mismatch" >&2
    exit 1
  fi
  if ! grep -q 'checksum mismatch' "$log"; then
    echo "verify: install.sh mismatch error does not mention the checksum:" >&2
    cat "$log" >&2
    exit 1
  fi

  # An unsupported platform must be refused before anything is downloaded.
  fakebin="${install_tmp}/fakebin"
  mkdir -p "$fakebin"
  cat >"${fakebin}/uname" <<'SHIM'
#!/bin/sh
case "$1" in
  -m) printf 'x86_64\n' ;;
  *) printf 'Darwin\n' ;;
esac
SHIM
  chmod +x "${fakebin}/uname"
  if PATH="${fakebin}:${PATH}" LUNATE_INSTALL_BASE_URL="file://${fixture}" \
    sh install.sh >"$log" 2>&1; then
    echo "verify: install.sh accepted macOS on x86_64" >&2
    exit 1
  fi
  if ! grep -q 'install.ps1' "$log"; then
    echo "verify: install.sh refusal does not point Windows users at install.ps1:" >&2
    cat "$log" >&2
    exit 1
  fi
fi

step "tool package roundtrip"
tool_tmp="$(verify_tmp)"
dotnet pack src/Lunate.Coding/Lunate.Coding.csproj -c "$CONFIGURATION" -o "${tool_tmp}/feed" --nologo
dotnet tool install --tool-path "${tool_tmp}/tools" --add-source "${tool_tmp}/feed" lunate --version "$version"
tool_bin="${tool_tmp}/tools/lunate"
if [ "${RID#win}" != "$RID" ]; then
  tool_bin="${tool_bin}.exe"
fi
tool_version="$("$tool_bin" --version)"
if [ "${tool_version%%+*}" != "$version" ]; then
  echo "verify: tool roundtrip reports ${tool_version}, expected ${version}" >&2
  exit 1
fi

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
