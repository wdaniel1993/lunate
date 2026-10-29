#!/usr/bin/env bash
# Refuses a pull request that is ready for review while its commits still carry a `wip(` subject.
#
# The queue driver commits a stalled session's leftovers as `wip(<card>): ...` so that nothing is
# lost when a session dies mid-edit. That is a safety net for a draft, not a deliverable: the commit
# message is the only durable record of what a change is, and `wip(t_abc): Onboarding 5/5` says
# nothing to the person reviewing it. A session that resumes the work rewrites the subject into a
# conventional commit; until it does, the pull request stays in draft, where this check does not run.
#
# Only subjects are inspected. A `wip(` in a body is prose; a subject beginning with `wip(` is the
# marker the driver writes and the one thing this check must catch.
#
# Usage:
#   scripts/no-wip-commits.sh              # compares against origin/main
#   scripts/no-wip-commits.sh <base-ref>   # or $BASE_REF in CI
#   scripts/no-wip-commits.sh --self-test

set -euo pipefail

subject_is_wip() {
  case "$1" in
    'wip('*) return 0 ;;
    *) return 1 ;;
  esac
}

check() {
  local base="$1" bad=0 subject
  if ! git rev-parse --verify --quiet "origin/${base}" >/dev/null; then
    printf 'no-wip-commits: origin/%s is not present, nothing to compare\n' "$base" >&2
    return 0
  fi
  while IFS= read -r subject; do
    [ -n "$subject" ] || continue
    if subject_is_wip "$subject"; then
      printf 'no-wip-commits: %s\n' "$subject" >&2
      bad=1
    fi
  done < <(git log --format=%s "origin/${base}..HEAD")

  if [ "$bad" -ne 0 ]; then
    cat >&2 <<'MESSAGE'
no-wip-commits: this pull request is ready for review but carries wip commits.
Rewrite each subject into a conventional commit that says what changed and why, then push:
    git commit --amend -m "feat(area): ..."            # the head commit
    GIT_EDITOR=true git rebase -i origin/main          # an older one
A wip commit is a safety net for a draft; it must not reach review.
MESSAGE
    return 1
  fi
  return 0
}

self_test() {
  local tmp failures=0
  tmp="$(mktemp -d)"
  trap 'rm -rf "$tmp"' RETURN

  git -C "$tmp" init -q -b main
  git -C "$tmp" config user.email "self-test@example.invalid"
  git -C "$tmp" config user.name "self test"
  git -C "$tmp" commit -q --allow-empty -m "chore: base"
  git -C "$tmp" update-ref refs/remotes/origin/main HEAD

  expect() {
    local want="$1" name="$2"
    local got=0
    ( cd "$tmp" && check main ) >/dev/null 2>&1 || got=1
    if [ "$got" -ne "$want" ]; then
      printf 'no-wip-commits self-test: %s — expected exit %s, got %s\n' "$name" "$want" "$got" >&2
      failures=$((failures + 1))
    fi
  }

  git -C "$tmp" commit -q --allow-empty -m "feat(scope): a real change"
  expect 0 "a conventional subject passes"

  git -C "$tmp" commit -q --allow-empty -m "wip(t_deadbeef): half a card"
  expect 1 "a wip subject fails"

  # The marker is the subject prefix, not the word: prose and near-misses must not trip it.
  git -C "$tmp" reset -q --hard origin/main
  git -C "$tmp" commit -q --allow-empty -m "docs: wipe the cache before running"
  expect 0 "a subject that merely contains 'wip' passes"

  git -C "$tmp" commit -q --allow-empty -m "docs: explain the driver" -m "The driver writes wip(t_x): ..."
  expect 0 "a wip( mention in the body passes"

  if [ "$failures" -ne 0 ]; then
    printf 'no-wip-commits self-test: %s failure(s)\n' "$failures" >&2
    return 1
  fi
  printf 'no-wip-commits self-test: 4 passed\n'
  return 0
}

case "${1:-}" in
  --self-test) self_test ;;
  "") check "${BASE_REF:-main}" ;;
  *) check "$1" ;;
esac
