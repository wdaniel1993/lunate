#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
. "${SCRIPT_DIR}/lib.sh"
. "${SCRIPT_DIR}/test-parallel.sh"

cd "${SCRIPT_DIR}/.."

# Which gate to run. `full` is the whole thing (build, publish, both test passes, install/tool
# roundtrips, formatting, public API); `docs` is the prose profile, and it deliberately runs no
# build and no test. The two are a closed set: a typo must fail here rather than quietly run the
# longest gate or, worse, the shortest one.
PROFILE="full"
while [ $# -gt 0 ]; do
  case "$1" in
    --profile)
      PROFILE="${2:-}"
      shift 2
      ;;
    --profile=*)
      PROFILE="${1#*=}"
      shift
      ;;
    *)
      echo "verify: unknown argument '$1' (usage: verify.sh [--profile docs|full])" >&2
      exit 2
      ;;
  esac
done
case "$PROFILE" in
  docs | full) ;;
  *)
    echo "verify: unknown profile '$PROFILE' (expected docs or full)" >&2
    exit 2
    ;;
esac

BUDGET_MS="${BUDGET_MS:-150}"
# Whether a budget breach fails this leg. macOS shared runners are bimodal: the floor is ~80-240 ms
# when the machine is quiet and ~310-330 ms when it is not, which no single budget separates from a
# real regression. The leg still measures and prints (and uploads) the number; enforcement belongs to
# a leg that does nothing else, which is what `perf (macos-latest)` in ci.yml exists for.
STARTUP_BUDGET_ENFORCE="${STARTUP_BUDGET_ENFORCE:-1}"
CONFIGURATION="${CONFIGURATION:-Release}"
PUBLISH_DIR="artifacts/publish"

# Slots per test pass. The two culture passes run beside each other, so the
# machine sees at most 2 x JOBS test processes (4 by default) instead of the
# unbounded fan-out of a solution-wide test run.
JOBS="${LUNATE_VERIFY_JOBS:-0}"
case "$JOBS" in
  '' | *[!0-9]*)
    echo "verify: LUNATE_VERIFY_JOBS must be a positive integer" >&2
    exit 1
    ;;
esac
if [ "$JOBS" -lt 0 ]; then
  echo "verify: LUNATE_VERIFY_JOBS must be zero or a positive integer" >&2
  exit 1
fi

# Assemblies that spawn processes or manipulate process-wide state keep a
# serial lane inside a pass; see scripts/test-parallel.sh for the measurement
# that backs the choice. The compile tests spawn the published single-file
# binary and restore a reference pack with it, so they own their lane too.
SERIAL_TEST_PROJECTS="Lunate.Coding.Tests Lunate.Agent.Tests Lunate.Extensibility.Compile.Tests"

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

# A path that crosses into a native Windows process — a nuget.config value, LUNATE_HOME — has to
# be written the way Windows sees it: MSYS rewrites command arguments, never file contents or the
# environment, so a `/tmp/...` value points the CLI at C:\tmp and at a feed that is not there.
native_path() {
  if command -v cygpath >/dev/null 2>&1; then
    cygpath -m "$1"
  else
    printf '%s\n' "$1"
  fi
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

# The docs profile: prose only. No build, no publish, no tests and no startup measurement, so a
# docs or proposal pull request cannot go red on a flake in the suite it never runs. What prose can
# break is (a) the generated regions drifting from the code they mirror, (b) the docs tree becoming
# a build input or a test's subject, (c) Markdown lint, and (d) the OpenSpec tree. Those are the four
# steps; `openspec validate` dominates the wall clock.
docs_profile() {
  step "tools"
  if ! command -v python3 >/dev/null 2>&1; then
    echo "verify: python3 is required for the docs profile (scripts/docs-sync); install Python 3 and re-run" >&2
    exit 1
  fi
  if ! command -v node >/dev/null 2>&1; then
    echo "verify: node is required for the documentation lint step; install Node.js (npx runs the pinned markdownlint-cli2)" >&2
    exit 1
  fi
  if ! command -v openspec >/dev/null 2>&1; then
    echo "verify: the openspec CLI is required for the docs profile; install the pinned version with: npm install -g @fission-ai/openspec@1.14.0" >&2
    exit 1
  fi

  step "generated docs regions"
  python3 "${SCRIPT_DIR}/docs-sync" --check

  step "docs are not build inputs"
  bash "${SCRIPT_DIR}/no-docs-references.sh"

  step "docs lint"
  npx --yes markdownlint-cli2@0.23.3

  step "openspec"
  openspec validate --all --strict

  echo "verify: OK (docs profile)"
}

if [ "$PROFILE" = "docs" ]; then
  docs_profile
  exit 0
fi

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
dotnet build lunate.slnx -c "$CONFIGURATION" --nologo

step "provider runtime assets"
# Providers are compile-private to Lunate.Ai, but their runtime assets must reach
# the app output or provider construction fails at run time.
for assembly in Anthropic OpenAI Microsoft.Extensions.AI; do
  if [ ! -f "src/Lunate.Coding/bin/${CONFIGURATION}/net10.0/${assembly}.dll" ]; then
    echo "verify: ${assembly}.dll is missing from the Lunate.Coding build output" >&2
    exit 1
  fi
done

# Publish and measure the startup budget BEFORE the test suite: the same gate, taken on a
# quiet machine. Measured after fifteen minutes of tests, the 300 ms median is reading the
# runner's thermal and scheduler state, not the product (ADR-0005 keeps the budget itself).
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
if ! BUDGET_MS="$BUDGET_MS" "${SCRIPT_DIR}/perf.sh" "$BINARY"; then
  if [ "$STARTUP_BUDGET_ENFORCE" = "1" ]; then
    exit 1
  fi
  echo "startup: measured but not enforced on this leg (STARTUP_BUDGET_ENFORCE=0)"
fi

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
# The pooled path is opt-in (LUNATE_VERIFY_JOBS=2 or more): it overlaps the two culture
# passes and bounds each pass's pool, which is ~2 minutes faster locally but leans on the
# runner harder. CI keeps the solution-wide run below, which is what the gate has always
# used; three timing-sensitive suites proved flaky under the pooled load on the Windows
# runner (see the PR), so the default stays the proven path.
if [ "$JOBS" -eq 0 ]; then
  run_tests "test" dotnet test --solution lunate.slnx -c "$CONFIGURATION" --filter-not-trait Category=Perf
  step "test (de-AT culture)"
  LANG=de_AT.UTF-8 LC_ALL=de_AT.UTF-8 run_tests "test (de-AT culture)" dotnet test --solution lunate.slnx -c "$CONFIGURATION" --no-build --filter-not-trait Category=Perf
else
test_missing=""
while IFS= read -r project; do
  assembly="${project%/*}/bin/${CONFIGURATION}/net10.0/$(basename "$project" .csproj).dll"
  if [ ! -f "$assembly" ]; then
    test_missing="${test_missing} ${assembly}"
  fi
done < <(verify_test_projects)
if [ -n "$test_missing" ]; then
  echo "verify: test assemblies missing from the ${CONFIGURATION} build:${test_missing}" >&2
  echo "verify: run the build step first (dotnet build lunate.slnx -c ${CONFIGURATION})" >&2
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
  echo "verify: no test projects found in lunate.slnx — test discovery regression?" >&2
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
fi

step "path index budget"
run_tests "path index budget" dotnet test --project tests/Lunate.Coding.Tests/Lunate.Coding.Tests.csproj -c "$CONFIGURATION" --no-build --filter-trait Category=Perf

step "write verifier budget"
run_tests "write verifier budget" dotnet test --project tests/Lunate.Roslyn.Tests/Lunate.Roslyn.Tests.csproj -c "$CONFIGURATION" --no-build --filter-trait Category=Perf

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
# The feed page is a product surface: 0.1.0 shipped with "License missing" and "Readme missing",
# so the package `pack` just wrote has to declare a licence, carry the readme it names, and link
# the repository. The check reads the .nupkg, not the intermediate nuspec in obj/, because the
# archive is what the feed receives. Same check for the extension package below.
"${SCRIPT_DIR}/check-package-metadata.sh" "${tool_tmp}/feed/lunate.${version}.nupkg" --id lunate
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

step "extension package roundtrip"
# The release ships an extension package beside the tool, so the bundle `dotnet pack` writes —
# lunate.package.json at the root, the extension under extension/, no lib/ — is a product artifact,
# not a test fixture. Pack the sample, then install it from a folder feed with the published binary
# exactly as the release smoke installs it from nuget.org: version pin, record, verification,
# uninstall. The smoke proves the feed; this proves the artifact on every commit, on every OS.
package_tmp="$(verify_tmp)"
package_id="Lunate.Extensions.Subagents"
repo_root="$(pwd)"
binary="$(cd "$(dirname "$BINARY")" && pwd)/$(basename "$BINARY")"
# The feed and the store are read by the native binary, so both are written in its dialect:
# Pack also gets the native feed, so bash and the CLI agree on one directory.
feed="$(native_path "${package_tmp}/feed")"
store="$(native_path "${package_tmp}/home")"
dotnet pack extensions/subagents/SubagentsExtension/SubagentsExtension.csproj \
  -c "$CONFIGURATION" -o "$feed" --nologo
# The extension's feed page is part of the release too, and its metadata must survive changes to
# the tool's: this is the same contract, asserted on the package the release ships.
"${SCRIPT_DIR}/check-package-metadata.sh" \
  "${feed}/Lunate.Extensions.Subagents.${version}.nupkg" --id Lunate.Extensions.Subagents
mkdir -p "${package_tmp}/worktree"
cat >"${package_tmp}/worktree/nuget.config" <<XML
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="${feed}" />
  </packageSources>
  <config>
    <add key="globalPackagesFolder" value="$(native_path "${package_tmp}/packages")" />
  </config>
</configuration>
XML
cd "${package_tmp}/worktree"
LUNATE_HOME="$store" "$binary" install "${package_id}@${version}"
LUNATE_HOME="$store" "$binary" packages --verify
LUNATE_HOME="$store" "$binary" uninstall "$package_id"
# The assertions look at the scratch directory through bash, where it is still a POSIX path.
if [ -e "${package_tmp}/home/extensions/subagents" ]; then
  echo "verify: the extension package roundtrip left ${package_tmp}/home/extensions/subagents behind" >&2
  exit 1
fi
if [ -n "$(find "${package_tmp}/home" -name install.json 2>/dev/null)" ]; then
  echo "verify: the extension package roundtrip left an install record behind" >&2
  exit 1
fi
cd "$repo_root"

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
dotnet format style lunate.slnx --verify-no-changes --no-restore
dotnet format analyzers lunate.slnx --verify-no-changes --no-restore

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
