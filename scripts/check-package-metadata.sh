#!/usr/bin/env bash
# Fails when a packed package cannot render a complete feed page: no licence, no readme, no
# repository link. The subject is the .nupkg `dotnet pack` produced — the file the feed receives —
# not the nuspec under obj/: that is an intermediate whose <files> list pack consumes, and Windows
# writes it differently from Unix, so reading it says nothing about what the archive carries
# (PR #235, run 40635300935: both Windows legs failed on the intermediate while the package was
# complete). 0.1.0 shipped without a licence and without a readme; those are the two warnings NuGet
# raised, and the archive is where both are decided.
#
# usage: check-package-metadata.sh <package.nupkg> [--id <package-id>]
#        check-package-metadata.sh --self-test
set -euo pipefail

usage() {
  echo "usage: check-package-metadata.sh <package.nupkg> [--id <package-id>]" >&2
  echo "       check-package-metadata.sh --self-test" >&2
  exit 2
}

# Prints the text of the first <name ...>…</name> element, empty when the element is absent.
element_text() {
  sed -n "s:.*<$1[^>]*>\(.*\)</$1>.*:\1:p" "$2" | head -n 1
}

# Prints the attributes of the first self-closing <name ... /> element, empty when absent.
element_attributes() {
  sed -n "s:.*<$1 \(.*\)/>.*:\1:p" "$2" | head -n 1
}

check_package() {
  local package="$1" expected_id="$2" failures=0
  local work nuspec package_id license readme repository

  if [ ! -f "$package" ]; then
    echo "check-package-metadata: no package at ${package}" >&2
    echo "check-package-metadata: pack the project first — dotnet pack writes <id>.<version>.nupkg to its -o directory" >&2
    return 1
  fi

  work="$(mktemp -d "${TMPDIR:-/tmp}/lunate-package-metadata.XXXXXX")"
  if ! unzip -q "$package" -d "$work"; then
    echo "check-package-metadata: ${package} is not a readable package archive" >&2
    rm -rf "$work"
    return 1
  fi

  # A package carries exactly one nuspec, beside the content it describes. The intermediate in
  # obj/ is a different file with a different shape; this is the one a feed actually publishes.
  nuspec=""
  for candidate in "$work"/*.nuspec; do
    if [ -f "$candidate" ]; then
      nuspec="$candidate"
      break
    fi
  done
  if [ -z "$nuspec" ]; then
    echo "check-package-metadata: ${package} carries no nuspec at its root" >&2
    echo "check-package-metadata: a package without a nuspec is not installable — repack it and read the pack log" >&2
    rm -rf "$work"
    return 1
  fi

  package_id="$(element_text id "$nuspec")"
  license="$(element_text license "$nuspec")"
  readme="$(element_text readme "$nuspec")"
  repository="$(element_attributes repository "$nuspec")"

  if [ -n "$expected_id" ] && [ "$package_id" != "$expected_id" ]; then
    echo "check-package-metadata: ${package} carries package id '${package_id}', expected '${expected_id}'" >&2
    failures=$((failures + 1))
  fi

  if [ -z "$license" ]; then
    echo "check-package-metadata: ${package} declares no licence" >&2
    echo "check-package-metadata: set PackageLicenseExpression (MIT for Lunate) in the project file" >&2
    failures=$((failures + 1))
  fi

  # The readme nuget.org renders has to be inside the package at its root: a declaration alone is
  # exactly NuGet's "Readme missing" warning.
  if [ -z "$readme" ]; then
    echo "check-package-metadata: ${package} declares no readme" >&2
    echo "check-package-metadata: set PackageReadmeFile and pack the file at the package root" >&2
    failures=$((failures + 1))
  elif [ "${readme#*/}" != "$readme" ] || [ ! -f "${work}/${readme}" ]; then
    echo "check-package-metadata: ${package} declares readme '${readme}' but carries no such entry at its root" >&2
    echo "check-package-metadata: add <None Include=\"...\" Pack=\"true\" PackagePath=\"/\" /> for ${readme}" >&2
    failures=$((failures + 1))
  fi

  case "$repository" in
    *url=\"http*type=\"git\"* | *type=\"git\"*url=\"http*) ;;
    *)
      echo "check-package-metadata: ${package} declares no repository url and type" >&2
      echo "check-package-metadata: set RepositoryUrl and RepositoryType (git) in the project file" >&2
      failures=$((failures + 1))
      ;;
  esac

  rm -rf "$work"
  if [ "$failures" -ne 0 ]; then
    return 1
  fi
  echo "check-package-metadata: ${package_id} carries a licence, readme '${readme}' at its root and a git repository"
}

SELF_TEST_DIR=""
self_test() {
  local dir cases=0
  if ! command -v git >/dev/null 2>&1; then
    echo "self-test: FAIL: git is required to build the fixture packages" >&2
    return 1
  fi
  SELF_TEST_DIR="$(mktemp -d "${TMPDIR:-/tmp}/lunate-package-metadata.XXXXXX")"
  dir="${SELF_TEST_DIR}"
  # The trap runs after this function returns, so it must not read a local variable.
  trap 'rm -rf "${SELF_TEST_DIR}"' EXIT

  # build_package <name> <nuspec> [root entries…] — the fixtures are real archives, because the
  # archive is what this check reads. The gate runs where `unzip` is guaranteed (Git Bash ships
  # it) but a zip writer is not, and git is always present: an index, write-tree and
  # `git archive --format=zip` write the same root entries `dotnet pack` does. An empty nuspec
  # name builds an archive with no nuspec at all.
  build_package() {
    local name="$1" nuspec="${2:-}" entry
    shift 2
    mkdir -p "${dir}/${name}"
    if [ -n "$nuspec" ]; then
      cp "$nuspec" "${dir}/${name}/lunate.nuspec"
    fi
    for entry in "$@"; do
      mkdir -p "$(dirname "${dir}/${name}/${entry}")"
      printf 'fixture\n' >"${dir}/${name}/${entry}"
    done
    git -c core.autocrlf=false -C "${dir}/${name}" init -q
    git -c core.autocrlf=false -C "${dir}/${name}" add -A
    git -c core.autocrlf=false -C "${dir}/${name}" archive --format=zip \
      -o "${dir}/${name}.nupkg" "$(git -C "${dir}/${name}" write-tree)"
  }

  # expect_rejected <package> <case name> <expected words> [extra args…]
  expect_rejected() {
    local package="$1" name="$2" expected="$3" output status
    shift 3
    output="$(bash "${BASH_SOURCE[0]}" "${dir}/${package}.nupkg" "$@" 2>&1)" && status=0 || status=$?
    cases=$((cases + 1))
    if [ "$status" -eq 0 ]; then
      echo "self-test: FAIL: accepted a package with ${name}" >&2
      return 1
    fi
    if ! grep -Fq "$expected" <<<"$output"; then
      echo "self-test: FAIL: the rejection of ${name} does not name '${expected}':" >&2
      echo "$output" >&2
      return 1
    fi
  }

  mkdir -p "${dir}/fixtures"
  # The complete fixture is shaped like a packed package: the embedded nuspec has no <files>
  # element at all, because pack consumes that list. Reading obj/ instead of the package is how
  # the check went red on Windows for a package that was complete.
  cat >"${dir}/fixtures/complete.nuspec" <<'XML'
<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
  <metadata>
    <id>lunate</id>
    <version>0.1.0</version>
    <license type="expression">MIT</license>
    <readme>README.md</readme>
    <repository type="git" url="https://github.com/wdaniel1993/lunate" commit="0123456" />
  </metadata>
</package>
XML
  cat >"${dir}/fixtures/license-file.nuspec" <<'XML'
<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
  <metadata>
    <id>lunate</id>
    <license type="file">LICENSE.txt</license>
    <readme>README.md</readme>
    <repository type="git" url="https://github.com/wdaniel1993/lunate" />
  </metadata>
</package>
XML
  cat >"${dir}/fixtures/no-license.nuspec" <<'XML'
<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
  <metadata>
    <id>lunate</id>
    <readme>README.md</readme>
    <repository type="git" url="https://github.com/wdaniel1993/lunate" />
  </metadata>
</package>
XML
  cat >"${dir}/fixtures/no-readme.nuspec" <<'XML'
<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
  <metadata>
    <id>lunate</id>
    <license type="expression">MIT</license>
    <repository type="git" url="https://github.com/wdaniel1993/lunate" />
  </metadata>
</package>
XML
  cat >"${dir}/fixtures/no-repository-url.nuspec" <<'XML'
<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
  <metadata>
    <id>lunate</id>
    <license type="expression">MIT</license>
    <readme>README.md</readme>
    <repository type="git" commit="0123456" />
  </metadata>
</package>
XML
  cat >"${dir}/fixtures/nested-readme.nuspec" <<'XML'
<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
  <metadata>
    <id>lunate</id>
    <license type="expression">MIT</license>
    <readme>docs/README.md</readme>
    <repository type="git" url="https://github.com/wdaniel1993/lunate" />
  </metadata>
</package>
XML

  build_package complete "${dir}/fixtures/complete.nuspec" README.md
  build_package license-file "${dir}/fixtures/license-file.nuspec" README.md LICENSE.txt
  build_package no-license "${dir}/fixtures/no-license.nuspec" README.md
  build_package no-readme "${dir}/fixtures/no-readme.nuspec" README.md
  # The declaration is there and the file is not: NuGet's "Readme missing" page.
  build_package readme-not-packed "${dir}/fixtures/complete.nuspec"
  # Declared in a subdirectory, which is not where nuget.org renders the introduction.
  build_package nested-readme "${dir}/fixtures/nested-readme.nuspec" docs/README.md
  build_package no-repository-url "${dir}/fixtures/no-repository-url.nuspec" README.md
  build_package no-nuspec "" README.md

  for package in complete license-file; do
    if ! bash "${BASH_SOURCE[0]}" "${dir}/${package}.nupkg" --id lunate >/dev/null; then
      echo "self-test: FAIL: rejected the ${package} package" >&2
      return 1
    fi
    cases=$((cases + 1))
  done
  expect_rejected no-license "no licence" "declares no licence"
  expect_rejected no-readme "no readme" "declares no readme"
  expect_rejected readme-not-packed "an unpacked readme" "carries no such entry at its root"
  expect_rejected nested-readme "a readme outside the package root" "carries no such entry at its root"
  expect_rejected no-repository-url "no repository url" "no repository url and type"
  expect_rejected no-nuspec "no nuspec" "carries no nuspec at its root"
  expect_rejected complete "the wrong package id" "expected 'other'" --id other

  echo "self-test: OK (${cases} cases)"
}

if ! command -v unzip >/dev/null 2>&1; then
  echo "check-package-metadata: unzip is required to read a package; install it (Git Bash ships it)" >&2
  exit 1
fi

if [ $# -eq 0 ]; then
  usage
fi
if [ "$1" = "--self-test" ]; then
  self_test
  exit 0
fi

package="$1"
shift
expected_id=""
while [ $# -gt 0 ]; do
  case "$1" in
    --id)
      [ $# -ge 2 ] || usage
      expected_id="$2"
      shift 2
      ;;
    *)
      usage
      ;;
  esac
done

check_package "$package" "$expected_id"
