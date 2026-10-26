#!/usr/bin/env bash
# Fails a pull request that only archives a change.
#
# The archive belongs as the final commit of the change's own pull request: the same reviewer then
# sees the implementation and the bookkeeping that closes it, and an archive-only PR cannot be the
# only thing exercising the change. A standalone archive is still legitimate — a change that merged
# before its archive existed — but it is an exception, so it needs the `standalone-archive` label
# and a sentence in the body saying why.
#
# `openspec archive` does not only move files: it applies the change's deltas to openspec/specs, so
# an archive-only PR legitimately touches three prefixes — the moved change, the archive, and the
# specs. Renames are counted as delete + add, which is why the source prefix is in the list.
#
# Usage:
#   archive-only-check.sh --paths-file <file> [--labels a,b]
#   archive-only-check.sh --self-test
#
# The workflow passes the pull request's file list from the API, so this script needs no git
# history and no checkout depth.

set -euo pipefail

readonly ARCHIVE_PREFIX="openspec/changes/archive/"
readonly CHANGE_PREFIX="openspec/changes/"
readonly SPEC_PREFIX="openspec/specs/"
readonly LABEL="standalone-archive"

readonly FAILURE_MESSAGE="This pull request only archives a change. The archive belongs as the final commit of the change's own pull request, so the reviewer sees the implementation and the bookkeeping together. If this is a deliberate standalone archive (the change merged before its archive existed), add the 'standalone-archive' label and a sentence in the body saying why."

decide() {
    local paths="$1" labels="$2"
    local total=0 archive=0 foreign=0 line

    while IFS= read -r line; do
        [ -n "$line" ] || continue
        total=$((total + 1))
        case "$line" in
            "$ARCHIVE_PREFIX"*) archive=$((archive + 1)) ;;
            "$CHANGE_PREFIX"* | "$SPEC_PREFIX"*) ;;
            *) foreign=$((foreign + 1)) ;;
        esac
    done <<<"$paths"

    if [ "$total" -eq 0 ]; then
        echo "PASS: the pull request changes no files"
        return 0
    fi

    if [ "$archive" -eq 0 ]; then
        echo "PASS: no archive in this pull request"
        return 0
    fi

    if [ "$foreign" -gt 0 ]; then
        echo "PASS: the archive rides with other changes"
        return 0
    fi

    case ",$labels," in
        *",$LABEL,"*)
            echo "PASS: archive-only, labelled $LABEL"
            return 0
            ;;
    esac

    echo "FAIL: $FAILURE_MESSAGE"
    return 1
}

self_test() {
    local tmp status=0
    tmp="$(mktemp -d)"
    # Bash 3.2 (macOS) has no RETURN trap, so the cases run in their own function and the scratch
    # directory is removed once, on both outcomes.
    _self_test_cases "$tmp" || status=$?
    rm -rf "$tmp"
    return "$status"
}

_self_test_cases() {
    local tmp="$1"

    # 1. An archive on its own is refused.
    printf '%s\n' \
        "openspec/changes/archive/2026-10-25-a/proposal.md" \
        "openspec/specs/tui/spec.md" >"$tmp/archive"
    if decide "$(cat "$tmp/archive")" "" >"$tmp/out"; then
        echo "self-test: an unlabelled archive-only PR must fail" >&2
        return 1
    fi
    grep -q "only archives a change" "$tmp/out" || {
        echo "self-test: the failure must explain the rule" >&2
        return 1
    }

    # 2. The explicit exception passes.
    decide "$(cat "$tmp/archive")" "bug,$LABEL" >/dev/null || {
        echo "self-test: a labelled archive-only PR must pass" >&2
        return 1
    }

    # 3. An archive that moves without a spec delta is still archive-only.
    printf '%s\n' "openspec/changes/archive/2026-10-25-a/tasks.md" >"$tmp/move"
    if decide "$(cat "$tmp/move")" "" >/dev/null; then
        echo "self-test: an archive-only PR without spec deltas must fail" >&2
        return 1
    fi

    # 4. An archive riding with code is the shape the rule wants.
    printf '%s\n' \
        "openspec/changes/archive/2026-10-25-a/tasks.md" \
        "src/Lunate.Coding/DraftEditor.cs" >"$tmp/mixed"
    decide "$(cat "$tmp/mixed")" "" >/dev/null || {
        echo "self-test: an archive with code must pass" >&2
        return 1
    }

    # 5. No archive at all is none of this check's business.
    printf '%s\n' "docs/guide.md" >"$tmp/plain"
    decide "$(cat "$tmp/plain")" "" >/dev/null || {
        echo "self-test: a PR without an archive must pass" >&2
        return 1
    }

    echo "self-test: 5/5"
}

paths_file=""
labels=""

while [ $# -gt 0 ]; do
    case "$1" in
        --paths-file)
            paths_file="${2:-}"
            shift 2
            ;;
        --labels)
            labels="${2:-}"
            shift 2
            ;;
        --self-test)
            self_test
            exit $?
            ;;
        *)
            echo "archive-only-check: unknown argument '$1'" >&2
            exit 2
            ;;
    esac
done

if [ -z "$paths_file" ] || [ ! -f "$paths_file" ]; then
    echo "archive-only-check: --paths-file <file> is required" >&2
    exit 2
fi

decide "$(cat "$paths_file")" "$labels"
