#!/usr/bin/env python3
import argparse
import re
import sys
from collections import defaultdict
from pathlib import Path


REPOSITORY_ROOT = Path(__file__).resolve().parents[1]
PRIMARY_ADR = re.compile(r"^(\d{4})-(?!addendum-).+\.md$")


def display_path(path: Path) -> str:
    return path.relative_to(REPOSITORY_ROOT).as_posix()


def check(directory: Path) -> list[str]:
    numbers: dict[int, list[Path]] = defaultdict(list)
    for path in sorted(directory.iterdir()):
        match = PRIMARY_ADR.match(path.name)
        if path.is_file() and match is not None:
            numbers[int(match.group(1))].append(path)

    diagnostics = []
    for number, paths in sorted(numbers.items()):
        if len(paths) > 1:
            names = ", ".join(display_path(path) for path in paths)
            diagnostics.append(f"duplicate ADR-{number:04d}: {names}")

    ordered_numbers = sorted(numbers)
    if ordered_numbers and ordered_numbers[0] > 1:
        first_number = ordered_numbers[0]
        first_path = display_path(numbers[first_number][0])
        diagnostics.append(f"missing ADR-0001–{first_number - 1:04d} before {first_path}")
    for previous, current in zip(ordered_numbers, ordered_numbers[1:]):
        if current > previous + 1:
            previous_path = display_path(numbers[previous][-1])
            current_path = display_path(numbers[current][0])
            missing = ", ".join(
                f"ADR-{number:04d}" for number in range(previous + 1, current)
            )
            diagnostics.append(
                f"missing {missing} between {previous_path} and {current_path}"
            )

    if diagnostics:
        diagnostics.append(f"next free number: {max(numbers) + 1:04d}")
    return diagnostics


def main() -> int:
    parser = argparse.ArgumentParser(description="Check primary ADR numbering.")
    parser.add_argument("--directory", type=Path, default=REPOSITORY_ROOT / "adr")
    args = parser.parse_args()

    diagnostics = check(args.directory)
    if diagnostics:
        for diagnostic in diagnostics:
            print(f"ADR numbering: {diagnostic}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
