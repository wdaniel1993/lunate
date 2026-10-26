#!/usr/bin/env bash
# Fails when the docs tree is a build input or a test reads it.
#
# The docs profile of scripts/verify.sh lints prose and validates OpenSpec, and it does not build or
# run a single test. That is only sound while nothing under docs/ is anything but prose: a project
# that includes docs/**, or a test that resolves the repository root to read a docs file, turns the
# docs into a build input again, and a docs-only pull request could then break the build while the
# cheap profile stays green. This check is the guard against that rot.
#
# What counts as a read:
#   1. a *.csproj / *.props / *.targets item or a *.sln line naming a docs path -- a build input;
#   2. a test file that resolves the repository root (lunate.slnx, FindRepositoryRoot,
#      RepositoryRoot) and names a docs path -- the shape every docs-reading test takes;
#   3. a File/Directory call on the same line as a docs path, root or no root.
#
# What does not: a docs path handed to an in-memory fixture or a rendered value -- AddFile,
# AddDirectory, AddTree, CompletionItem.File. That is a workspace or a display string, not a file
# the test reads from the repository, and the allowance is printed so it is never silent.
#
# Usage:
#   scripts/no-docs-references.sh
#   scripts/no-docs-references.sh --self-test

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

DOCS_PATH='"docs[/"\\]'
ROOT_MARKERS='lunate\.sln|FindRepositoryRoot|RepositoryRoot'
FILESYSTEM_CALL='(File|Directory)\.[A-Za-z]+\([^;]*"docs[/"\\]'

findings=0
scanned=0

report() {
  printf 'no-docs-references: %s\n' "$1" >&2
}

# Prunes what is never a source of truth: VCS data, build output, package caches.
find_sources() {
  local root="$1" pattern="$2"
  find "$root" \
    \( -name .git -o -name bin -o -name obj -o -name node_modules -o -name artifacts \) -prune \
    -o -type f -name "$pattern" -print
}

scan_build_file() {
  local root="$1" file="$2" hit number text
  local hits=""
  case "$file" in
    *.sln) hits="$(grep -nE 'docs[/\\]' "$file" || true)" ;;
    *) hits="$(grep -nE '(Include|Update)="[^"]*docs[/\\]' "$file" || true)" ;;
  esac
  [ -n "$hits" ] || return 0
  while IFS= read -r hit; do
    [ -n "$hit" ] || continue
    number="${hit%%:*}"
    text="${hit#*:}"
    report "FAIL: ${file#"${root}"/}:${number} makes the docs tree a build input: ${text}"
    findings=$((findings + 1))
  done <<<"$hits"
}

scan_test_file() {
  local root="$1" file="$2" hit number text
  local candidates rootish=0
  scanned=$((scanned + 1))
  candidates="$(grep -nE "$DOCS_PATH" "$file" || true)"
  [ -n "$candidates" ] || return 0
  if grep -qE "$ROOT_MARKERS" "$file"; then
    rootish=1
  fi
  while IFS= read -r hit; do
    [ -n "$hit" ] || continue
    number="${hit%%:*}"
    text="${hit#*:}"
    case "$text" in
      # A fixture or a rendered value: the string never becomes a path in this repository.
      *'.AddFile("docs'* | *'.AddDirectory("docs'* | *'.AddTree("docs'* | *'CompletionItem.File("docs'*)
        printf 'no-docs-references: note: %s:%s is a fixture path, not a repository read\n' \
          "${file#"${root}"/}" "$number"
        continue
        ;;
    esac
    if [ "$rootish" -eq 1 ]; then
      report "FAIL: ${file#"${root}"/}:${number} reads a docs path from the repository root: ${text}"
      findings=$((findings + 1))
      continue
    fi
    if printf '%s' "$text" | grep -qE "$FILESYSTEM_CALL"; then
      report "FAIL: ${file#"${root}"/}:${number} reads a docs path: ${text}"
      findings=$((findings + 1))
    fi
  done <<<"$candidates"
}

check_tree() {
  local root="$1" file build_files test_files
  findings=0
  scanned=0

  build_files="$(find_sources "$root" '*.csproj'; find_sources "$root" '*.props'; find_sources "$root" '*.targets'; find_sources "$root" '*.sln')"
  if [ -n "$build_files" ]; then
    while IFS= read -r file; do
      [ -n "$file" ] || continue
      scan_build_file "$root" "$file"
    done <<<"$build_files"
  fi

  if [ -d "${root}/tests" ]; then
    test_files="$(find "${root}/tests" \
      \( -name bin -o -name obj -o -name node_modules -o -name artifacts \) -prune \
      -o -type f -name '*.cs' -print)"
    if [ -n "$test_files" ]; then
      while IFS= read -r file; do
        [ -n "$file" ] || continue
        scan_test_file "$root" "$file"
      done <<<"$test_files"
    fi
  fi

  # A check that scanned nothing cannot fail, so it must fail here instead.
  if [ "$scanned" -eq 0 ]; then
    report "FAIL: no test source under ${root}/tests; the check would pass vacuously"
    return 1
  fi

  if [ "$findings" -ne 0 ]; then
    report "${findings} docs reference(s); the docs tree must stay prose that no build or test reads"
    return 1
  fi
  printf 'no-docs-references: OK (%s test file(s) scanned)\n' "$scanned"
  return 0
}

self_test() {
  local scratch status=0 case_dir
  scratch="$(mktemp -d)"
  _self_test_cases "$scratch" || status=$?
  rm -rf "$scratch"
  return "$status"
}

_fixture_tree() {
  local dir="$1"
  mkdir -p "$dir/src/App" "$dir/tests/App.Tests" "$dir/docs"
  printf '<Project>\n</Project>\n' >"$dir/src/App/App.csproj"
  printf 'public sealed class T\n{\n    void M() { }\n}\n' >"$dir/tests/App.Tests/PlainTests.cs"
}

_self_test_cases() {
  local scratch="$1" out

  # 1. A tree with no docs reference passes.
  case_dir="${scratch}/clean"
  _fixture_tree "$case_dir"
  if ! out="$(check_tree "$case_dir" 2>&1)"; then
    printf 'self-test: a clean tree must pass, said: %s\n' "$out" >&2
    return 1
  fi

  # 2. A project that includes docs/** is a build input.
  case_dir="${scratch}/build-input"
  _fixture_tree "$case_dir"
  printf '<Project>\n    <ItemGroup>\n        <None Include="../../docs/guide.md" />\n    </ItemGroup>\n</Project>\n' \
    >"$case_dir/src/App/App.csproj"
  if out="$(check_tree "$case_dir" 2>&1)"; then
    echo "self-test: a csproj that includes docs/ must fail" >&2
    return 1
  fi
  case "$out" in
    *"makes the docs tree a build input"*) ;;
    *)
      printf 'self-test: the failure must name the build input, said: %s\n' "$out" >&2
      return 1
      ;;
  esac

  # 3. A props file that includes docs/** is a build input too.
  case_dir="${scratch}/props-input"
  _fixture_tree "$case_dir"
  printf '<Project>\n    <ItemGroup>\n        <Content Include="docs/**" />\n    </ItemGroup>\n</Project>\n' \
    >"$case_dir/Directory.Build.props"
  if check_tree "$case_dir" >/dev/null 2>&1; then
    echo "self-test: a props file that includes docs/ must fail" >&2
    return 1
  fi

  # 4. A test that resolves the repository root and reads a docs path fails.
  case_dir="${scratch}/root-read"
  _fixture_tree "$case_dir"
  printf 'public sealed class T\n{\n    string M() => File.ReadAllText(Path.Combine(FindRepositoryRoot(), "docs", "guide.md"));\n}\n' \
    >"$case_dir/tests/App.Tests/GuideTests.cs"
  if out="$(check_tree "$case_dir" 2>&1)"; then
    echo "self-test: a root-resolving test that reads docs/ must fail" >&2
    return 1
  fi
  case "$out" in
    *"from the repository root"*) ;;
    *)
      printf 'self-test: the failure must name the root read, said: %s\n' "$out" >&2
      return 1
      ;;
  esac

  # 5. A File call on a docs path fails even without a root helper.
  case_dir="${scratch}/direct-read"
  _fixture_tree "$case_dir"
  printf 'public sealed class T\n{\n    string M() => File.ReadAllText(Path.Combine(Path.GetTempPath(), "docs", "x.md"));\n}\n' \
    >"$case_dir/tests/App.Tests/DirectTests.cs"
  if check_tree "$case_dir" >/dev/null 2>&1; then
    echo "self-test: a File call on a docs path must fail" >&2
    return 1
  fi

  # 6. An in-memory fixture is allowed, and the allowance is printed.
  case_dir="${scratch}/fixture"
  _fixture_tree "$case_dir"
  printf 'public sealed class T\n{\n    void M() => new FakeFiles().AddFile("docs/guide.md", "# guide");\n}\n' \
    >"$case_dir/tests/App.Tests/FixtureTests.cs"
  if ! out="$(check_tree "$case_dir" 2>&1)"; then
    printf 'self-test: a fixture docs path must pass, said: %s\n' "$out" >&2
    return 1
  fi
  case "$out" in
    *"is a fixture path"*) ;;
    *)
      printf 'self-test: the allowance must be printed, said: %s\n' "$out" >&2
      return 1
      ;;
  esac

  # 7. A rendered value or a comment is not a read.
  case_dir="${scratch}/rendered"
  _fixture_tree "$case_dir"
  printf 'public sealed class T\n{\n    // docs/guide.md is the plan.\n    void M(string text) => Assert.Contains("  docs/guide.md", text);\n}\n' \
    >"$case_dir/tests/App.Tests/RenderedTests.cs"
  if ! check_tree "$case_dir" >/dev/null 2>&1; then
    echo "self-test: a rendered docs string must pass" >&2
    return 1
  fi

  # 8. A tree with no test source cannot pass: the check would be vacuous.
  case_dir="${scratch}/empty"
  mkdir -p "$case_dir/src/App" "$case_dir/docs"
  printf '<Project>\n</Project>\n' >"$case_dir/src/App/App.csproj"
  if out="$(check_tree "$case_dir" 2>&1)"; then
    echo "self-test: a tree with no test sources must fail" >&2
    return 1
  fi
  case "$out" in
    *vacuous*) ;;
    *)
      printf 'self-test: the vacuity failure must say so, said: %s\n' "$out" >&2
      return 1
      ;;
  esac

  echo "self-test: 8/8"
}

case "${1:-}" in
  --self-test)
    self_test
    exit $?
    ;;
  "")
    ;;
  *)
    echo "no-docs-references: unknown argument '$1'" >&2
    exit 2
    ;;
esac

cd "$REPO_ROOT"
check_tree "$REPO_ROOT"
