#!/usr/bin/env bash
# Derives every package version from a release tag, and proves the rule the release follows:
# an RC tag never publishes a stable version of anything.
#
#   release-versions.sh v0.2.0-rc.1
#   release-versions.sh --self-test
#
# Output, one KEY=VALUE per line:
#   TOOL=0.2.0-rc.1            the `lunate` tool and the Subagents bundle (the product version)
#   CONTRACTS=1.15.0-rc.1      Lunate.Contracts and Lunate.Extensibility.Abstractions (the API
#                              version from Directory.Build.props, plus the tag's suffix)
#   PREVIEW=0.2.0-rc.1         Lunate.Agent and Lunate.Extensibility.Testing: the product version
#                              while it is a prerelease, otherwise <version>-preview.1
#   TEMPLATE=1.15.0-rc.1       the contract version a copied template pins
#
# `.github/workflows/pack.yml` runs --self-test before packing, then compares the packed file names
# against the derivation for the tree's own version: a package that packs under a different version
# than the rule says is a release-blocking mismatch, not a warning.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
PROPS="${REPO_ROOT}/Directory.Build.props"

api_version() {
    sed -n 's:.*<ContractApiVersion>\(.*\)</ContractApiVersion>.*:\1:p' "${PROPS}" | head -n 1
}

derive() {
    local tag="$1" version suffix api
    version="${tag#v}"
    if ! printf '%s' "${version}" | grep -Eq '^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$'; then
        echo "release-versions: '${tag}' is not a release tag (expected v<major>.<minor>.<patch>[-<prerelease>])" >&2
        return 1
    fi
    api="$(api_version)"
    if [ -z "${api}" ]; then
        echo "release-versions: Directory.Build.props has no <ContractApiVersion>" >&2
        return 1
    fi
    case "${version}" in
        *-*) suffix="-${version#*-}" ;;
        *) suffix="" ;;
    esac
    printf 'TOOL=%s\n' "${version}"
    printf 'CONTRACTS=%s%s\n' "${api}" "${suffix}"
    if [ -n "${suffix}" ]; then
        printf 'PREVIEW=%s\n' "${version}"
    else
        printf 'PREVIEW=%s-preview.1\n' "${version}"
    fi
    printf 'TEMPLATE=%s%s\n' "${api}" "${suffix}"
}

self_test() {
    local failures=0 checks=0 want got key

    check() {
        local label="$1" tag="$2" key_name="$3" expected="$4" out
        out="$(derive "${tag}")"
        got="$(printf '%s\n' "${out}" | sed -n "s/^${key_name}=//p")"
        checks=$((checks + 1))
        if [ "${got}" != "${expected}" ]; then
            echo "release-versions: FAIL: ${label}: ${key_name}=${got:-<missing>}, expected ${expected}" >&2
            failures=$((failures + 1))
        fi
    }

    # An RC tag: nothing stable leaves the pipeline.
    check "RC" v0.2.0-rc.1 TOOL 0.2.0-rc.1
    check "RC" v0.2.0-rc.1 CONTRACTS 1.15.0-rc.1
    check "RC" v0.2.0-rc.1 PREVIEW 0.2.0-rc.1
    check "RC" v0.2.0-rc.1 TEMPLATE 1.15.0-rc.1

    # The invariant, stated as such: every version derived from an RC tag carries a prerelease.
    checks=$((checks + 1))
    while IFS='=' read -r key value; do
        case "${value}" in
            *-*) ;;
            *)
                echo "release-versions: FAIL: RC derived a stable ${key}=${value}" >&2
                failures=$((failures + 1))
                ;;
        esac
    done < <(derive v0.2.0-rc.1)

    # A final tag: the contracts are stable, the previews carry the preview marker.
    check "final" v0.2.0 CONTRACTS 1.15.0
    check "final" v0.2.0 PREVIEW 0.2.0-preview.1
    check "final" v0.2.0 TEMPLATE 1.15.0
    check "final" v0.2.0 TOOL 0.2.0

    # A bad tag is refused rather than guessed at.
    checks=$((checks + 1))
    if derive "0.2" >/dev/null 2>&1; then
        echo "release-versions: FAIL: a malformed tag was accepted" >&2
        failures=$((failures + 1))
    fi

    if [ "${failures}" -ne 0 ]; then
        echo "release-versions: ${failures} of ${checks} checks failed" >&2
        return 1
    fi
    echo "release-versions: OK (${checks} checks)"
}

if [ "${1:-}" = "--self-test" ]; then
    self_test
    exit $?
fi

if [ "$#" -ne 1 ]; then
    echo "usage: release-versions.sh <tag> | --self-test" >&2
    exit 2
fi

derive "$1"
