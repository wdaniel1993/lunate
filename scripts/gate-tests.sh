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

# The measurement itself: hyperfine 2.x writes its statistics under
# .results[].summary.time_wall_clock, 1.x at the top level, and a JSON that fits
# neither once sailed through as an empty string and an empty comparison — a gate
# that could not read its own number passed. These three shims pin all of it down
# without needing the real hyperfine.
echo "gate-tests: perf.sh must read the hyperfine 2.x JSON, gate on its floor, and refuse a JSON it cannot read"
SHIM_DIR="$(mktemp -d)"
shim_hyperfine() {
  cat >"${SHIM_DIR}/hyperfine" <<'SHIM'
#!/usr/bin/env bash
if [ "$1" = "--version" ]; then
  echo "hyperfine 2.0.0"
  exit 0
fi
out=""
while [ $# -gt 0 ]; do
  case "$1" in
    --export-json)
      out="$2"
      shift 2
      ;;
    *) shift ;;
  esac
done
cat >"$out" <<'JSON'
{"results":[{"command":"x","summary":{"time_wall_clock":{"unit":"second","count":3,"mean":0.011,"stddev":0.001,"median":0.012,"min":0.010,"max":0.013}}}]}
JSON
exit 0
SHIM
  chmod +x "${SHIM_DIR}/hyperfine"
}
shim_hyperfine
if ! PATH="${SHIM_DIR}:$PATH" BUDGET_MS=100 PERF_RESULTS=artifacts/perf-shim-pass.json \
  "${SCRIPT_DIR}/perf.sh" "$BINARY" >/dev/null; then
  echo "gate-tests: FAIL: perf.sh did not read a hyperfine 2.x measurement" >&2
  rm -rf "$SHIM_DIR"
  exit 1
fi
if PATH="${SHIM_DIR}:$PATH" BUDGET_MS=5 PERF_RESULTS=artifacts/perf-shim-floor.json \
  "${SCRIPT_DIR}/perf.sh" "$BINARY" >/dev/null 2>&1; then
  echo "gate-tests: FAIL: perf.sh passed with a floor above the budget (10 ms against 5 ms)" >&2
  rm -rf "$SHIM_DIR"
  exit 1
fi
cat >"${SHIM_DIR}/hyperfine" <<'SHIM'
#!/usr/bin/env bash
if [ "$1" = "--version" ]; then
  echo "hyperfine 2.0.0"
  exit 0
fi
out=""
while [ $# -gt 0 ]; do
  case "$1" in
    --export-json)
      out="$2"
      shift 2
      ;;
    *) shift ;;
  esac
done
printf '{"results":[{"command":"x"}]}' >"$out"
exit 0
SHIM
chmod +x "${SHIM_DIR}/hyperfine"
if PATH="${SHIM_DIR}:$PATH" PERF_RESULTS=artifacts/perf-shim-unreadable.json \
  "${SCRIPT_DIR}/perf.sh" "$BINARY" >/dev/null 2>&1; then
  echo "gate-tests: FAIL: perf.sh passed without a readable measurement" >&2
  rm -rf "$SHIM_DIR"
  exit 1
fi
rm -rf "$SHIM_DIR"

FORMAT_DIR=""
SHIPPED_DIR=""
WAIT_DIR=""
cleanup() {
  if [ -n "$FORMAT_DIR" ]; then
    rm -rf "$FORMAT_DIR"
  fi
  if [ -n "$SHIPPED_DIR" ]; then
    rm -rf "$SHIPPED_DIR"
  fi
  if [ -n "$WAIT_DIR" ]; then
    rm -rf "$WAIT_DIR"
  fi
}
trap cleanup EXIT
# Route signals into a plain exit so a killed run still fires the EXIT trap.
trap 'exit 129' HUP
trap 'exit 130' INT
trap 'exit 143' TERM

# Stale scratch sweep: a hard-killed run can leave a .gate-* dir in the repo
# root; remove ones older than 4 hours (a live run never lasts that long).
find "${SCRIPT_DIR}/.." -maxdepth 1 -type d -name '.gate-*' -mmin +240 -exec rm -rf {} + 2>/dev/null || true

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

echo "gate-tests: shipped-API gate must fail for an uncommitted Shipped change"
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
if (cd "$SHIPPED_DIR" && . "${SCRIPT_DIR}/lib.sh" && check_shipped_api_unchanged) >/dev/null 2>&1; then
  echo "gate-tests: FAIL: shipped-API gate passed for an uncommitted Shipped change" >&2
  exit 1
fi

echo "gate-tests: shipped-API gate must fail for a committed Shipped change on a branch"
git -C "$SHIPPED_DIR" add -A
git -C "$SHIPPED_DIR" commit -qm "change"
if (cd "$SHIPPED_DIR" && . "${SCRIPT_DIR}/lib.sh" && check_shipped_api_unchanged) >/dev/null 2>&1; then
  echo "gate-tests: FAIL: shipped-API gate passed for a committed Shipped change on a branch" >&2
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

echo "gate-tests: wait-for-nuget.sh must wait for a version the fixture feed indexes late"
# A file:// URL is read by curl, which may be the native build: on Windows an MSYS path (/tmp/...)
# is not a path it can open, so every fixture URL is written the way Windows sees it.
fixture_url() {
  if command -v cygpath >/dev/null 2>&1; then
    printf 'file://%s\n' "$(cygpath -m "$1")"
  else
    printf 'file://%s\n' "$1"
  fi
}
wait_for_nuget="${SCRIPT_DIR}/wait-for-nuget.sh"
WAIT_DIR="$(mktemp -d)"
fc="${WAIT_DIR}/flatcontainer"
reg="${WAIT_DIR}/registration"
fixture_id="lunate.fixture.pkg"
fc_url="$(fixture_url "$fc")"
reg_url="$(fixture_url "$reg")"
mkdir -p "${fc}/${fixture_id}" "${reg}/${fixture_id}"
# The feed starts with an older version only: index, content and registration page all lack the
# version the run asks for.
printf '{"versions":["0.1.0"]}\n' >"${fc}/${fixture_id}/index.json"
printf '{"items":[{"@id":"%s/%s/page1.json","items":[{"catalogEntry":{"version":"0.1.0"}}]},' \
  "$reg_url" "$fixture_id" >"${reg}/${fixture_id}/index.json"
printf '{"@id":"%s/%s/page2.json","@type":"catalog:CatalogPage"}]}\n' \
  "$reg_url" "$fixture_id" >>"${reg}/${fixture_id}/index.json"
printf '{"items":[{"catalogEntry":{"version":"0.1.0"}}]}\n' >"${reg}/${fixture_id}/page2.json"
# The version appears while the script polls — index, then content, then the linked registration
# page — which is how nuget.org indexes a fresh push.
(
  sleep 2
  printf '{"versions":["0.1.0","0.2.0"]}\n' >"${fc}/${fixture_id}/index.json"
  mkdir -p "${fc}/${fixture_id}/0.2.0"
  printf 'nupkg\n' >"${fc}/${fixture_id}/0.2.0/${fixture_id}.0.2.0.nupkg"
  printf '{"items":[{"catalogEntry":{"version":"0.1.0"}},{"catalogEntry":{"version":"0.2.0"}}]}\n' \
    >"${reg}/${fixture_id}/page2.json"
) &
indexer=$!
wait_log="${WAIT_DIR}/wait.log"
wait_rc=0
bash "$wait_for_nuget" --package Lunate.Fixture.Pkg --version 0.2.0 \
  --budget-seconds 30 --interval-seconds 1 \
  --flatcontainer-base "$fc_url" --registration-base "$reg_url" >"$wait_log" 2>&1 || wait_rc=$?
wait "$indexer"
if [ "$wait_rc" -ne 0 ]; then
  echo "gate-tests: FAIL: wait-for-nuget.sh gave up on a version the feed indexed mid-budget:" >&2
  cat "$wait_log" >&2
  exit 1
fi
if ! grep -q 'not listed yet after' "$wait_log"; then
  echo "gate-tests: FAIL: wait-for-nuget.sh reported no wait, so the fixture was already indexed:" >&2
  cat "$wait_log" >&2
  exit 1
fi
if ! grep -q 'lists Lunate.Fixture.Pkg 0.2.0 after' "$wait_log"; then
  echo "gate-tests: FAIL: wait-for-nuget.sh does not name the package and version it waited for:" >&2
  cat "$wait_log" >&2
  exit 1
fi
if ! grep -q 'registration .*3 version(s) across 1 linked page(s)' "$wait_log"; then
  echo "gate-tests: FAIL: wait-for-nuget.sh did not read the linked registration page:" >&2
  cat "$wait_log" >&2
  exit 1
fi

echo "gate-tests: wait-for-nuget.sh must fail naming the feed state when the version never appears"
printf '{"versions":["0.1.0"]}\n' >"${fc}/${fixture_id}/index.json"
wait_rc=0
bash "$wait_for_nuget" --package Lunate.Fixture.Pkg --version 0.3.0 \
  --budget-seconds 3 --interval-seconds 1 \
  --flatcontainer-base "$fc_url" --registration-base "$reg_url" >"$wait_log" 2>&1 || wait_rc=$?
if [ "$wait_rc" -eq 0 ]; then
  echo "gate-tests: FAIL: wait-for-nuget.sh passed although the feed never listed the version" >&2
  cat "$wait_log" >&2
  exit 1
fi
for expected in 'did not list Lunate.Fixture.Pkg 0.3.0 within 3s' '0.3.0 absent' \
  'not checked (the index does not list the version)'; do
  if ! grep -qF "$expected" "$wait_log"; then
    echo "gate-tests: FAIL: wait-for-nuget.sh budget report does not name '$expected':" >&2
    cat "$wait_log" >&2
    exit 1
  fi
done

echo "gate-tests: wait-for-nuget.sh must not pass a listed version whose content is missing"
printf '{"versions":["0.1.0","0.4.0"]}\n' >"${fc}/${fixture_id}/index.json"
wait_rc=0
bash "$wait_for_nuget" --package Lunate.Fixture.Pkg --version 0.4.0 \
  --budget-seconds 3 --interval-seconds 1 \
  --flatcontainer-base "$fc_url" --registration-base "$reg_url" >"$wait_log" 2>&1 || wait_rc=$?
if [ "$wait_rc" -eq 0 ]; then
  echo "gate-tests: FAIL: wait-for-nuget.sh passed although the .nupkg was not downloadable" >&2
  cat "$wait_log" >&2
  exit 1
fi
if ! grep -qF 'the .nupkg is not downloadable' "$wait_log"; then
  echo "gate-tests: FAIL: wait-for-nuget.sh budget report does not name the missing content:" >&2
  cat "$wait_log" >&2
  exit 1
fi
rm -rf "$WAIT_DIR"
WAIT_DIR=""

echo "gate-tests: check-package-metadata.sh self-test must pass"
if ! bash "${SCRIPT_DIR}/check-package-metadata.sh" --self-test >/dev/null; then
  echo "gate-tests: FAIL: check-package-metadata.sh accepted or misreported an incomplete package" >&2
  exit 1
fi

echo "gate-tests: the shard spec parser must accept i/n and reject junk, zero and out-of-range"
. "${SCRIPT_DIR}/test-parallel.sh"
shard_got="$(shard_spec_parse '2/4' | tr '\n' ' ')"
if [ "$shard_got" != "2 4 " ]; then
  echo "gate-tests: FAIL: shard_spec_parse '2/4' parsed as '$shard_got'" >&2
  exit 1
fi
for shard_bad in '2' '1/0' '0/2' '3/2' '4/2' 'a/2' '1/a' '1//2' '1/2/3' '' '  1/2'; do
  if shard_spec_parse "$shard_bad" >/dev/null 2>&1; then
    echo "gate-tests: FAIL: shard_spec_parse accepted '$shard_bad'" >&2
    exit 1
  fi
done

# One shard's slice of the real lunate.slnx: position filter with index/count.
shard_slice() {
  SHARD_I="$1" SHARD_N="$2" verify_shard_projects
}

echo "gate-tests: the project list must one-to-one match the solution's test projects"
partition_all="$(LC_ALL=C sort < <(verify_test_projects))"
partition_count="$(printf '%s\n' "$partition_all" | wc -l | tr -d ' ')"
if [ "$partition_count" -eq 0 ] || [ "$partition_count" -ne "$(grep -o 'Tests\.csproj' lunate.slnx | wc -l | tr -d ' ')" ]; then
  echo "gate-tests: FAIL: verify_test_projects reads $partition_count projects; lunate.slnx has $(grep -o 'Tests\.csproj' lunate.slnx | wc -l | tr -d ' ') (post-#179 extraction regression?)" >&2
  exit 1
fi
shard_union="$(printf '%s\n' "$(shard_slice 1 2)" "$(shard_slice 2 2)" | LC_ALL=C sort)"
if [ "$shard_union" != "$partition_all" ]; then
  echo "gate-tests: FAIL: shards 1/2+2/2 do not partition the test projects" >&2
  diff <(printf '%s\n' "$shard_union") <(printf '%s\n' "$partition_all") | head -20 >&2
  exit 1
fi
shard_union3="$(printf '%s\n' "$(shard_slice 1 3)" "$(shard_slice 2 3)" "$(shard_slice 3 3)" | LC_ALL=C sort)"
if [ "$shard_union3" != "$partition_all" ]; then
  echo "gate-tests: FAIL: shards 1/3..3/3 do not partition the test projects" >&2
  exit 1
fi
# Round-robin keeps the run balanced: neighbouring shard sizes differ by at most one.
shard_size_1="$(printf '%s\n' "$(shard_slice 1 2)" | grep -c . || true)"
shard_size_2="$(printf '%s\n' "$(shard_slice 2 2)" | grep -c . || true)"
if [ "$((shard_size_1 - shard_size_2 > 1 || shard_size_2 - shard_size_1 > 1))" -eq 1 ]; then
  echo "gate-tests: FAIL: shard sizes are unbalanced: $shard_size_1/$shard_size_2 for $partition_count projects" >&2
  exit 1
fi

echo "gate-tests: the shard module expression must be one built-assembly path per project, joined with ';'"
shard_modules_all="$(CONFIGURATION=Release SHARD_I=1 SHARD_N=1 verify_shard_modules)"
module_count="$(printf '%s\n' "$shard_modules_all" | tr ';' '\n' | grep -c .)"
if [ "$module_count" -ne "$partition_count" ]; then
  echo "gate-tests: FAIL: the 1/1 module expression carries $module_count entries for $partition_count projects" >&2
  exit 1
fi
case "$shard_modules_all" in
  *"/bin/Release/net10.0/Lunate.Tui.Tests.dll"*) ;;
  *)
    echo "gate-tests: FAIL: module expression is not a built-assembly path per project: $shard_modules_all" >&2
    exit 1
    ;;
esac
shard_modules_repeat="$(CONFIGURATION=Release SHARD_I=1 SHARD_N=1 verify_shard_modules)"
if [ "$shard_modules_all" != "$shard_modules_repeat" ]; then
  echo "gate-tests: FAIL: the module expression is not deterministic across calls" >&2
  exit 1
fi
module_union="$(printf '%s\n%s\n' "$(CONFIGURATION=Release SHARD_I=1 SHARD_N=2 verify_shard_modules | tr ';' '\n')" "$(CONFIGURATION=Release SHARD_I=2 SHARD_N=2 verify_shard_modules | tr ';' '\n')" | LC_ALL=C sort)"
module_union_all="$(printf '%s\n' "$shard_modules_all" | tr ';' '\n' | LC_ALL=C sort)"
if [ "$module_union" != "$module_union_all" ]; then
  echo "gate-tests: FAIL: the shard module expressions do not cover the solution set" >&2
  exit 1
fi

echo "gate-tests: a shard that would own zero test projects must fail instead of running nothing"
shard_rc=0
( CONFIGURATION=Release SHARD_I=25 SHARD_N=25 verify_shard_modules ) >/dev/null 2>&1 || shard_rc=$?
if [ "$shard_rc" -eq 0 ]; then
  echo "gate-tests: FAIL: shard 25/25 produced a module expression for zero projects" >&2
  exit 1
fi

echo "gate-tests: OK"
