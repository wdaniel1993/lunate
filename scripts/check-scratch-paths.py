#!/usr/bin/env python3
"""Fail a pull request that adds files under an agent's scratch directory.

Plans, ledgers and resume notes belong in `openspec/changes/<name>/` — the change folder is the
only plan format this repository reads, reviews and archives. A skill or tool that keeps its own
ledger directory produced `.superpowers/sdd/issue-241/progress.md` on `main` and with it a change
whose design lived outside OpenSpec and a spec that was hand-edited to catch up (#241/#313).

The list below is deliberately a registry of directories the queues are known to produce; add a new
one here when a new skill writes one, rather than letting it reach `main` first.

Usage:
    check-scratch-paths.py                     # diff origin/main...HEAD
    check-scratch-paths.py --base <rev>        # diff <rev>...HEAD
    check-scratch-paths.py --paths-file <file> # one path per line (tests)
"""

from __future__ import annotations

import argparse
import subprocess
import sys
from pathlib import Path

REPOSITORY_ROOT = Path(__file__).resolve().parents[1]

# Directories that are local scratch space, never repository content. `.worktrees/` and `.opencode/`
# are already ignored, but an ignore rule protects the working tree, not `main`: a `git add -f` or a
# harness that stages everything can still commit them, which is what this check is for.
SCRATCH_DIRECTORIES = (
    ".superpowers/",  # superpowers skills (subagent-driven development) write sd/<card>/progress.md
    ".worktrees/",  # per-card git worktrees
    ".opencode/",  # OpenCode session cache
    ".claude/",  # Claude Code session state
    ".codex/",  # Codex session state
    ".aider/",  # aider chat history
    ".sdd/",  # any other spec-driven-development ledger directory
)


def changed_paths(base: str | None, paths_file: str | None) -> list[str]:
    if paths_file:
        return [line.strip() for line in Path(paths_file).read_text().splitlines() if line.strip()]
    if not base:
        base = "origin/main"
    result = subprocess.run(
        ["git", "diff", "--name-only", "--diff-filter=A", f"{base}...HEAD"],
        cwd=REPOSITORY_ROOT,
        capture_output=True,
        text=True,
        check=False,
    )
    if result.returncode != 0:
        raise SystemExit(f"check-scratch-paths: git diff against {base} failed: {result.stderr.strip()}")
    return [line.strip() for line in result.stdout.splitlines() if line.strip()]


def check(paths: list[str]) -> list[str]:
    diagnostics = []
    for path in paths:
        # Strip the "./" prefix only — `lstrip("./")` would eat the leading dot of ".superpowers/".
        normalized = path[2:] if path.startswith("./") else path
        for directory in SCRATCH_DIRECTORIES:
            if normalized == directory.rstrip("/") or normalized.startswith(directory):
                diagnostics.append(
                    f"{path}: agent scratch file — plans and ledgers belong in openspec/changes/<name>/ "
                    f"(design.md, tasks.md or notes.md there), not in {directory}"
                )
                break
    return diagnostics


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base", help="git revision to diff against (default: origin/main)")
    parser.add_argument("--paths-file", help="read the path list from a file instead of git")
    arguments = parser.parse_args()

    diagnostics = check(changed_paths(arguments.base, arguments.paths_file))
    for line in diagnostics:
        print(f"check-scratch-paths: {line}", file=sys.stderr)
    if diagnostics:
        print(
            "check-scratch-paths: move the content into the change folder and delete the scratch path",
            file=sys.stderr,
        )
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
