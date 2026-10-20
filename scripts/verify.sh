#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
. "${SCRIPT_DIR}/lib.sh"
. "${SCRIPT_DIR}/test-parallel.sh"

cd "${SCRIPT_DIR}/.."

BUDGET_MS="${BUDGET_MS:-150}"
CONFIGURATION="${CONFIGURATION:-Release}"
PUBLISH_DIR="artifacts/publish"

# Slots per test pass. The two culture passes run beside each other, so the
# machine sees at most 2 x JOBS test processes (4 by default) instead of the
# unbounded fan-out of a solution-wide test run.
JOBS="${LUNATE_VERIFY_JOBS:-2}"
case "$JOBS" in
  '' | *[!0-9]*)
    echo "verify: LUNATE_VERIFY_JOBS must be a positive integer" >&2
    exit 1
    ;;
esac
if [ "$JOBS" -lt 1 ]; then
  echo "verify: LUNATE_VERIFY_JOBS must be a positive integer" >&2
  exit 1
fi

# Assemblies that spawn processes or manipulate process-wide state keep a
# serial lane inside a pass; see scripts/test-parallel.sh for the measurement
# that backs the choice.
SERIAL_TEST_PROJECTS="Lunate.Coding.Tests Lunate.Agent.Tests"

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
# summary line is "total: N"; a missing or zero count is a failure.
#
# Output goes to a file and is printed when the command ends — not through a
# pipe. `tee` ends only at EOF, and EOF needs every writer of the pipe to
# close; build workers (MSBuild node reuse, the compiler server) inherit the
# pipe and can outlive the command by design, stalling the step for minutes on
# Windows CI (probe-verified: VBCSCompiler alone held a captured pass open for
# 3m16s). A file has no such dependency: the step ends when the command ends.
run_tests() {
  local label="$1"
  shift
  local output status total
  output="$(mktemp "${VERIFY_TMP_ROOT}/out.XXXXXX")"
  set +e
  "$@" > "$output" 2>&1
  status=$?
  set -e
  cat "$output"
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

VERIFY_TMP_ROOT=""

# One scratch root per run, created and registered at top level — never inside a
# command substitution. The old design registered each dir from inside
# `verify_tmp` called via `$(...)`, so the registration happened in a subshell
# and the cleanup never saw the dirs: every run leaked its ~0.7 GB staging dirs
# even on success. verify_tmp now hands out subdirs inside this root.
VERIFY_TMP_ROOT="$(mktemp -d "${TMPDIR:-/tmp}/lunate-verify.XXXXXX")"

# Stale scratch sweep: a hard-killed run (timeout kill, closed terminal —
# anything that bypasses the traps below) can leave a lunate-verify.* root
# behind; remove ones older than 4 hours. A live gate run never lasts that
# long, and the name-prefix is ours by attribution. (Older anonymous tmp.*
# leftovers from before the prefix existed are swept by the Mac Mini's
# tmp-leak guard.)
find "${TMPDIR:-/tmp}" -maxdepth 1 -type d -name 'lunate-verify.*' -mmin +240 -exec rm -rf {} + 2>/dev/null || true

verify_tmp() {
  local dir
  dir="$(mktemp -d "${VERIFY_TMP_ROOT}/scratch.XXXXXX")"
  printf '%s\n' "$dir"
}

verify_cleanup() {
  if [ -n "$VERIFY_TMP_ROOT" ] && [ -d "$VERIFY_TMP_ROOT" ]; then
    rm -rf "$VERIFY_TMP_ROOT"
  fi
  VERIFY_TMP_ROOT=""
}

# The EXIT trap does the cleanup; route signals into a plain exit so a killed
# run still fires it. Without these, TERM/INT/HUP terminate bash without the
# EXIT trap and leave the full install-staging dir behind (~0.7 GB per run).
trap verify_cleanup EXIT
trap 'exit 129' HUP
trap 'exit 130' INT
trap 'exit 143' TERM

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
# The Category=Perf budget check is excluded here (and from the culture pass) and
# runs as its own step below, so the main passes stay fast and the budget is
# still enforced by the gate.
#
# The two culture passes are independent processes with independent
# environments, so they run at the same time instead of back to back. Each has
# its own bounded pool (LUNATE_VERIFY_JOBS, default 2); a serial assembly owns
# every slot of its pass. Output is still printed per assembly and in solution
# order by verify_check_test_pass. `--no-build` is safe because the build step
# above built every test project, and the missing-assembly check proves it.
test_missing=""
while IFS= read -r project; do
  assembly="${project%/*}/bin/${CONFIGURATION}/net10.0/$(basename "$project" .csproj).dll"
  if [ ! -f "$assembly" ]; then
    test_missing="${test_missing} ${assembly}"
  fi
done < <(verify_test_projects)
if [ -n "$test_missing" ]; then
  echo "verify: test assemblies missing from the ${CONFIGURATION} build:${test_missing}" >&2
  echo "verify: run the build step first (dotnet build lunate.sln -c ${CONFIGURATION})" >&2
  exit 1
fi

test_queue=()
while IFS= read -r item; do
  test_queue+=("$item")
done < <(verify_test_queue serial-first)
culture_queue=()
while IFS= read -r item; do
  culture_queue+=("$item")
done < <(verify_test_queue serial-last)
if [ "${#test_queue[@]}" -eq 0 ]; then
  echo "verify: no test projects found in lunate.sln — test discovery regression?" >&2
  exit 1
fi

test_root="${VERIFY_TMP_ROOT}/test-phase"
default_dir="${test_root}/default"
culture_dir="${test_root}/de-AT"
VERIFY_TEST_FAILURES=0
(verify_run_test_pass "$default_dir" "${test_queue[@]}") &
default_pid=$!
(LANG=de_AT.UTF-8 LC_ALL=de_AT.UTF-8 verify_run_test_pass "$culture_dir" "${culture_queue[@]}") &
culture_pid=$!

default_rc=0
culture_rc=0
wait "$default_pid" || default_rc=$?
wait "$culture_pid" || culture_rc=$?

verify_check_test_pass "$default_dir" "test"

step "test (de-AT culture)"
# Non-English culture pass (S-5 finding). Effective on macOS/Linux; Windows
# runners keep the OS culture (LANG is not honored there).
verify_check_test_pass "$culture_dir" "test (de-AT culture)"

if [ "$default_rc" -ne 0 ] || [ "$culture_rc" -ne 0 ]; then
  echo "verify: the test phase runner failed (default ${default_rc}, de-AT ${culture_rc})" >&2
  exit 1
fi
if [ "$VERIFY_TEST_FAILURES" -ne 0 ]; then
  exit 1
fi

step "path index budget"
run_tests "path index budget" dotnet test --project tests/Lunate.Coding.Tests/Lunate.Coding.Tests.csproj -c "$CONFIGURATION" --no-build --filter-trait Category=Perf

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
  prefix_flags="${install_tmp}/prefix-flags"
  bad_fixture="${install_tmp}/bad-fixture"
  bad_prefix="${install_tmp}/bad-prefix"
  log="${install_tmp}/install.log"
  mkdir -p "${fixture}/latest/download" "${fixture}/download/v${version}" \
    "$prefix" "$prefix_flags" "${bad_fixture}/latest/download" "$bad_prefix"

  install_asset="lunate-${RID}.tar.gz"
  tar -czf "${fixture}/latest/download/${install_asset}" -C "${PUBLISH_DIR}/${RID}" lunate
  printf '%s  %s\n' "$(sha256_file "${fixture}/latest/download/${install_asset}")" "$install_asset" \
    >"${fixture}/latest/download/SHA256SUMS"
  cp "${fixture}/latest/download/${install_asset}" "${fixture}/download/v${version}/${install_asset}"
  cp "${fixture}/latest/download/SHA256SUMS" "${fixture}/download/v${version}/SHA256SUMS"

  # (a) Default install: the environment supplies only the releases base.
  if ! LUNATE_INSTALL_BASE_URL="file://${fixture}" LUNATE_INSTALL_PREFIX="$prefix" \
    LUNATE_INSTALL_VERSION= sh install.sh >"$log" 2>&1; then
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

  # (b) --version/--prefix must drive the composed URL and the target directory.
  if ! LUNATE_INSTALL_BASE_URL="file://${fixture}" LUNATE_INSTALL_PREFIX= \
    sh install.sh --version "v${version}" --prefix "$prefix_flags" >"$log" 2>&1; then
    echo "verify: install.sh failed with --version/--prefix against the fixture release:" >&2
    cat "$log" >&2
    exit 1
  fi
  installed_flags="$("${prefix_flags}/lunate" --version)"
  if [ "$installed_flags" != "$published" ]; then
    echo "verify: installed lunate reports ${installed_flags}, expected ${published}" >&2
    exit 1
  fi

  # (c) A tag with no release directory must fail even though latest/ exists:
  # the tag participates in the URL the script composes.
  if LUNATE_INSTALL_BASE_URL="file://${fixture}" LUNATE_INSTALL_PREFIX= \
    sh install.sh --version v9.9.9 --prefix "${install_tmp}/prefix-bad-tag" >"$log" 2>&1; then
    echo "verify: install.sh installed a nonexistent version v9.9.9 from the fixture" >&2
    exit 1
  fi
  if ! grep -q 'failed to download' "$log"; then
    echo "verify: install.sh did not fail on the nonexistent version download:" >&2
    cat "$log" >&2
    exit 1
  fi

  cp -R "${fixture}/." "$bad_fixture/"
  printf 'corrupt\n' >>"${bad_fixture}/latest/download/${install_asset}"
  if LUNATE_INSTALL_BASE_URL="file://${bad_fixture}" LUNATE_INSTALL_PREFIX="$bad_prefix" \
    LUNATE_INSTALL_VERSION= sh install.sh >"$log" 2>&1; then
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

step "packaging files"
for packaging_file in \
  packaging/homebrew/lunate.rb \
  packaging/scoop/lunate.json \
  packaging/winget/wdaniel1993.lunate.yaml \
  packaging/winget/wdaniel1993.lunate.locale.en-US.yaml \
  packaging/winget/wdaniel1993.lunate.installer.yaml; do
  if [ ! -f "$packaging_file" ]; then
    echo "verify: missing packaging file: ${packaging_file}" >&2
    exit 1
  fi
  if ! grep -Fq "$version" "$packaging_file"; then
    echo "verify: ${packaging_file} does not carry the current version ${version}" >&2
    exit 1
  fi
done

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
