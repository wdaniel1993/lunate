#!/usr/bin/env bash
# Fails when a packed nuspec cannot render a complete feed page: no licence, no readme, no
# repository link. The nuspec is the file `dotnet pack` writes before it zips the package
# (obj/<Configuration>/<id>.<version>.nuspec), so the gate checks the file that becomes the
# package's metadata rather than the page nuget.org builds from it. 0.1.0 shipped without a
# licence and without a readme; these are the two warnings NuGet raised, and the nuspec is where
# both are decided.
#
# usage: check-package-metadata.sh <nuspec> [--id <package-id>]
#        check-package-metadata.sh --self-test
set -euo pipefail

usage() {
  echo "usage: check-package-metadata.sh <nuspec> [--id <package-id>]" >&2
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

check_nuspec() {
  local nuspec="$1" expected_id="$2" failures=0
  local package_id license readme repository

  if [ ! -f "$nuspec" ]; then
    echo "check-package-metadata: no nuspec at ${nuspec}" >&2
    echo "check-package-metadata: pack the project first — dotnet pack writes obj/<Configuration>/<id>.<version>.nuspec" >&2
    return 1
  fi

  package_id="$(element_text id "$nuspec")"
  license="$(element_text license "$nuspec")"
  readme="$(element_text readme "$nuspec")"
  repository="$(element_attributes repository "$nuspec")"

  if [ -n "$expected_id" ] && [ "$package_id" != "$expected_id" ]; then
    echo "check-package-metadata: ${nuspec} carries package id '${package_id}', expected '${expected_id}'" >&2
    failures=$((failures + 1))
  fi

  if [ -z "$license" ]; then
    echo "check-package-metadata: ${nuspec} declares no licence" >&2
    echo "check-package-metadata: set PackageLicenseExpression (MIT for Lunate) in the project file" >&2
    failures=$((failures + 1))
  fi

  if [ -z "$readme" ]; then
    echo "check-package-metadata: ${nuspec} declares no readme" >&2
    echo "check-package-metadata: set PackageReadmeFile and pack the file at the package root" >&2
    failures=$((failures + 1))
  elif ! grep -Fq "target=\"/${readme}\"" "$nuspec"; then
    # A declared readme that is not packed is exactly NuGet's "Readme missing" warning.
    echo "check-package-metadata: ${nuspec} declares readme '${readme}' but no <file> packs it" >&2
    echo "check-package-metadata: add <None Include=\"...\" Pack=\"true\" PackagePath=\"/\" /> for ${readme}" >&2
    failures=$((failures + 1))
  fi

  case "$repository" in
    *url=\"http*type=\"git\"* | *type=\"git\"*url=\"http*) ;;
    *)
      echo "check-package-metadata: ${nuspec} declares no repository url and type" >&2
      echo "check-package-metadata: set RepositoryUrl and RepositoryType (git) in the project file" >&2
      failures=$((failures + 1))
      ;;
  esac

  if [ "$failures" -ne 0 ]; then
    return 1
  fi
  echo "check-package-metadata: ${package_id} declares a licence, readme '${readme}' and a git repository"
}

SELF_TEST_DIR=""
self_test() {
  local dir output status cases=0
  SELF_TEST_DIR="$(mktemp -d "${TMPDIR:-/tmp}/lunate-package-metadata.XXXXXX")"
  dir="${SELF_TEST_DIR}"
  # The trap runs after this function returns, so it must not read a local variable.
  trap 'rm -rf "${SELF_TEST_DIR}"' EXIT

  write_nuspec() {
    cat >"${dir}/$1"
  }

  # expect_rejected <fixture> <case name> <expected words> [extra args…]
  expect_rejected() {
    local fixture="$1" name="$2" expected="$3"
    shift 3
    output="$(bash "${BASH_SOURCE[0]}" "${dir}/${fixture}" "$@" 2>&1)" && status=0 || status=$?
    cases=$((cases + 1))
    if [ "$status" -eq 0 ]; then
      echo "self-test: FAIL: accepted a nuspec with ${name}" >&2
      return 1
    fi
    if ! grep -Fq "$expected" <<<"$output"; then
      echo "self-test: FAIL: the rejection of ${name} does not name '${expected}':" >&2
      echo "$output" >&2
      return 1
    fi
  }

  write_nuspec complete.nuspec <<'XML'
<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd">
  <metadata>
    <id>lunate</id>
    <version>0.1.0</version>
    <license type="expression">MIT</license>
    <readme>README.md</readme>
    <repository type="git" url="https://github.com/wdaniel1993/lunate" commit="0123456" />
  </metadata>
  <files>
    <file src="/repo/README.md" target="/README.md" />
  </files>
</package>
XML
  write_nuspec no-license.nuspec <<'XML'
<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd">
  <metadata>
    <id>lunate</id>
    <readme>README.md</readme>
    <repository type="git" url="https://github.com/wdaniel1993/lunate" />
  </metadata>
  <files>
    <file src="/repo/README.md" target="/README.md" />
  </files>
</package>
XML
  write_nuspec no-readme.nuspec <<'XML'
<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd">
  <metadata>
    <id>lunate</id>
    <license type="expression">MIT</license>
    <repository type="git" url="https://github.com/wdaniel1993/lunate" />
  </metadata>
</package>
XML
  write_nuspec readme-not-packed.nuspec <<'XML'
<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd">
  <metadata>
    <id>lunate</id>
    <license type="expression">MIT</license>
    <readme>README.md</readme>
    <repository type="git" url="https://github.com/wdaniel1993/lunate" />
  </metadata>
</package>
XML
  write_nuspec no-repository-url.nuspec <<'XML'
<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd">
  <metadata>
    <id>lunate</id>
    <license type="expression">MIT</license>
    <readme>README.md</readme>
    <repository type="git" commit="0123456" />
  </metadata>
  <files>
    <file src="/repo/README.md" target="/README.md" />
  </files>
</package>
XML

  if ! bash "${BASH_SOURCE[0]}" "${dir}/complete.nuspec" --id lunate >/dev/null; then
    echo "self-test: FAIL: rejected a complete nuspec" >&2
    return 1
  fi
  cases=$((cases + 1))
  expect_rejected no-license.nuspec "no licence" "declares no licence"
  expect_rejected no-readme.nuspec "no readme" "declares no readme"
  expect_rejected readme-not-packed.nuspec "an unpacked readme" "no <file> packs it"
  expect_rejected no-repository-url.nuspec "no repository url" "no repository url and type"
  expect_rejected complete.nuspec "the wrong package id" "expected 'other'" --id other

  echo "self-test: OK (${cases} cases)"
}

if [ $# -eq 0 ]; then
  usage
fi
if [ "$1" = "--self-test" ]; then
  self_test
  exit 0
fi

nuspec="$1"
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

check_nuspec "$nuspec" "$expected_id"
