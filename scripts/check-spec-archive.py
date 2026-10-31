#!/usr/bin/env python3
"""Fail a pull request that edits `openspec/specs/**` without archiving a change in the same PR.

The source-of-truth specs are produced by `openspec archive <change>`: the command syncs the
change's delta into `openspec/specs/` and moves the change into `openspec/changes/archive/`. Editing
a spec by hand skips the delta, the design and the record — and that is exactly how the bash guard
(#241/#313) reached `main` with 33 hand-written spec lines and no change behind them.

A legitimate spec change therefore always carries its archive entry in the same diff. The check does
not judge the entry's content; `openspec validate --all --strict` does that.

Usage:
    check-spec-archive.py                     # diff origin/main...HEAD
    check-spec-archive.py --base <rev>        # diff <rev>...HEAD
    check-spec-archive.py --paths-file <file> # one path per line (tests)
"""

from __future__ import annotations

import argparse
import subprocess
import sys
from pathlib import Path

REPOSITORY_ROOT = Path(__file__).resolve().parents[1]
SPECS = "openspec/specs/"
ARCHIVE = "openspec/changes/archive/"


def changed_paths(base: str | None, paths_file: str | None) -> list[str]:
    if paths_file:
        return [line.strip() for line in Path(paths_file).read_text().splitlines() if line.strip()]
    if not base:
        base = "origin/main"
    result = subprocess.run(
        ["git", "diff", "--name-only", f"{base}...HEAD"],
        cwd=REPOSITORY_ROOT,
        capture_output=True,
        text=True,
        check=False,
    )
    if result.returncode != 0:
        raise SystemExit(f"check-spec-archive: git diff against {base} failed: {result.stderr.strip()}")
    return [line.strip() for line in result.stdout.splitlines() if line.strip()]


def check(paths: list[str]) -> list[str]:
    specs = [path for path in paths if path.startswith(SPECS)]
    if not specs:
        return []
    if any(path.startswith(ARCHIVE) for path in paths):
        return []
    joined = ", ".join(specs[:3]) + ("…" if len(specs) > 3 else "")
    return [
        f"{joined}: spec edited by hand — specs are synced by `openspec archive <change>`; "
        f"add openspec/changes/archive/<YYYY-MM-DD>-<name>/ (and its delta) in the same pull request"
    ]


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base", help="git revision to diff against (default: origin/main)")
    parser.add_argument("--paths-file", help="read the path list from a file instead of git")
    arguments = parser.parse_args()

    diagnostics = check(changed_paths(arguments.base, arguments.paths_file))
    for line in diagnostics:
        print(f"check-spec-archive: {line}", file=sys.stderr)
    return 1 if diagnostics else 0


if __name__ == "__main__":
    sys.exit(main())
