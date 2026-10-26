#!/usr/bin/env bash
# Decides which verify profile a pull request needs: `docs` or `full`.
#
# The full gate builds, publishes, runs the whole suite twice and measures startup -- twenty
# minutes of it -- and a proposal or a page cannot break any of that. Those pull requests get the
# docs profile instead: markdownlint, `openspec validate --all --strict`, the generated-region check
# and the guard that nothing under docs/ became a build input or a test's subject.
#
# The allow-list is deny-by-default. Only prose paths qualify:
#   docs/**, adr/**, openspec/** (proposals, specs, archives), root-level *.md (incl. CHANGELOG.md).
# Anything else -- a source file, a test, a project, the templates, and any .github/workflows change
# -- means `full`. A rename is read as both of its paths, so a rename out of the docs tree (or into
# it, which deletes a code path) is `full`; a deletion in the tree is `docs`, a deletion outside it
# is `full`. An empty or unreadable change set is `full` too: a plan that cannot name what changed
# must not claim the cheap profile.
#
# Usage:
#   ci-plan.sh --paths-file <file>   # `git diff --name-status -M` output
#   ci-plan.sh --self-test
#
# Prints one line, `profile=docs` or `profile=full`, on stdout -- ready for $GITHUB_OUTPUT -- and the
# reason on stderr.

set -euo pipefail

readonly ALLOWED_PREFIXES="docs/ adr/ openspec/"

allowed() {
    local path="$1" prefix
    for prefix in $ALLOWED_PREFIXES; do
        case "$path" in
            "$prefix"*) return 0 ;;
        esac
    done
    # Root-level Markdown only: a *.md inside a code folder is a folder's README, not a page.
    case "$path" in
        */*) return 1 ;;
        *.md) return 0 ;;
    esac
    return 1
}

# One `git diff --name-status -M` line per change: a status, then one path (two for a rename).
# The separator is a tab, so it is built here instead of being spelled in a pattern.
decide() {
    local paths="$1" line total=0 foreign=0
    while IFS= read -r line; do
        [ -n "$line" ] || continue
        total=$((total + 1))
        _decide_line "$line" || foreign=$((foreign + 1))
    done <<<"$paths"

    if [ "$total" -eq 0 ]; then
        echo "full"
        echo "ci-plan: no changed paths; failing closed to the full gate" >&2
        return 0
    fi
    if [ "$foreign" -gt 0 ]; then
        echo "full"
        echo "ci-plan: $foreign change(s) outside the docs allow-list" >&2
        return 0
    fi
    echo "docs"
    echo "ci-plan: every changed path is prose" >&2
}

# Splits the path fields of one change line; every field has to be allowed.
_decide_line() {
    local line="$1" tab rest path foreign=0
    tab="$(printf '\t')"
    rest="${line#*"$tab"}"
    if [ "$rest" = "$line" ]; then
        echo "ci-plan: '$line' has no path; failing closed" >&2
        return 1
    fi
    while [ -n "$rest" ]; do
        case "$rest" in
            *"$tab"*)
                path="${rest%%"$tab"*}"
                rest="${rest#*"$tab"}"
                ;;
            *)
                path="$rest"
                rest=""
                ;;
        esac
        [ -n "$path" ] || continue
        if ! allowed "$path"; then
            echo "ci-plan: '$path' is not prose, so the full gate runs" >&2
            foreign=1
        fi
    done
    return "$foreign"
}

self_test() {
    local scratch status=0
    scratch="$(mktemp -d)"
    # Bash 3.2 (macOS) has no RETURN trap, so the cases run in their own function and the scratch
    # directory is removed once, on both outcomes.
    _self_test_cases "$scratch" || status=$?
    rm -rf "$scratch"
    return "$status"
}

_expect() {
    local scratch="$1" name="$2" expected="$3" lines="$4" got
    printf '%b' "$lines" >"$scratch/paths"
    got="$(decide "$(cat "$scratch/paths")" 2>/dev/null)"
    if [ "$got" != "$expected" ]; then
        printf 'self-test: %s must be %s, got %s\n' "$name" "$expected" "$got" >&2
        return 1
    fi
}

_self_test_cases() {
    local scratch="$1"

    # 1. A proposal is prose: the whole proposal shape (proposal, design, tasks, deltas).
    _expect "$scratch" "a proposal-only pull request" "docs" \
        'A\topenspec/changes/docs-gate/proposal.md\nA\topenspec/changes/docs-gate/design.md\nA\topenspec/changes/docs-gate/tasks.md\nA\topenspec/changes/docs-gate/specs/tooling/spec.md\n' || {
        echo "self-test: a proposal-only PR must run the docs profile" >&2
        return 1
    }

    # 2. Documentation beside source is a code change.
    _expect "$scratch" "docs plus src" "full" \
        'M\tdocs/guide.md\nM\tsrc/Lunate.Tui/Interaction/KeyBinding.cs\n' || return 1

    # 3. The root README on its own.
    _expect "$scratch" "the root README" "docs" 'M\tREADME.md\n' || return 1

    # 4. An archive ride: the moved change and the specs it applies to.
    _expect "$scratch" "an archive" "docs" \
        'D\topenspec/changes/a/proposal.md\nA\topenspec/changes/archive/2026-10-26-a/proposal.md\nM\topenspec/specs/tui/spec.md\n' || return 1

    # 5. The workflow itself always gets the full gate: the plan must not be able to skip its own
    #    change unnoticed.
    _expect "$scratch" "a workflow change" "full" 'M\t.github/workflows/ci.yml\n' || return 1

    # 6. A rename is both paths, so moving a page into src/ is a code change.
    _expect "$scratch" "a rename out of docs" "full" 'R100\tdocs/guide.md\tsrc/guide.md\n' || return 1

    # 7. A root-level non-Markdown file is not prose.
    _expect "$scratch" "a root JSON file" "full" 'A\tdocs-allowlist.json\n' || return 1

    # 8. A Markdown file below the root that is not in an allowed tree is code-adjacent.
    _expect "$scratch" "a package README" "full" 'M\tsrc/Lunate.Tui/README.md\n' || return 1

    # 9. A deletion inside the tree is prose; one outside it is not.
    _expect "$scratch" "a deleted page" "docs" 'D\tdocs/old.md\n' || return 1
    _expect "$scratch" "a deleted source file" "full" 'D\tsrc/Lunate.Tui/Old.cs\n' || return 1

    # 10. CHANGELOG.md and the ADRs are prose; an empty change set fails closed.
    _expect "$scratch" "the changelog" "docs" 'M\tCHANGELOG.md\n' || return 1
    _expect "$scratch" "an ADR" "docs" 'M\tadr/0018-docs-gate.md\n' || return 1
    _expect "$scratch" "no changes at all" "full" '' || return 1

    echo "self-test: 12/12"
}

paths_file=""

while [ $# -gt 0 ]; do
    case "$1" in
        --paths-file)
            paths_file="${2:-}"
            shift 2
            ;;
        --self-test)
            self_test
            exit $?
            ;;
        *)
            echo "ci-plan: unknown argument '$1'" >&2
            exit 2
            ;;
    esac
done

if [ -z "$paths_file" ] || [ ! -f "$paths_file" ]; then
    echo "ci-plan: --paths-file <file> is required" >&2
    exit 2
fi

profile="$(decide "$(cat "$paths_file")")"
echo "profile=${profile}"
