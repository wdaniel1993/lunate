import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
CHECKER = REPOSITORY_ROOT / "scripts" / "check-spec-archive.py"


def run_check(paths: list[str]) -> subprocess.CompletedProcess[str]:
    with tempfile.NamedTemporaryFile("w", suffix=".txt", delete=False) as handle:
        handle.write("\n".join(paths) + "\n")
        path = handle.name
    try:
        return subprocess.run(
            [sys.executable, str(CHECKER), "--paths-file", path],
            capture_output=True,
            text=True,
            check=False,
        )
    finally:
        Path(path).unlink(missing_ok=True)


class SpecArchiveTests(unittest.TestCase):
    def test_a_hand_edited_spec_is_refused(self) -> None:
        result = run_check(["openspec/specs/agent-tools/spec.md"])

        self.assertEqual(1, result.returncode)
        self.assertIn("spec edited by hand", result.stderr)
        self.assertIn("openspec archive <change>", result.stderr)

    def test_a_spec_with_its_archive_entry_is_allowed(self) -> None:
        result = run_check(
            [
                "openspec/specs/agent-tools/spec.md",
                "openspec/changes/archive/2026-10-30-bash-guard/specs/agent-tools/spec.md",
            ]
        )

        self.assertEqual(0, result.returncode, result.stderr)

    def test_an_archive_without_a_spec_change_is_allowed(self) -> None:
        # Archiving a change whose delta lands in a spec that is already merged touches only the
        # archive; nothing here should fire.
        result = run_check(["openspec/changes/archive/2026-10-30-bash-guard/proposal.md"])

        self.assertEqual(0, result.returncode)

    def test_code_changes_are_ignored(self) -> None:
        result = run_check(["src/Lunate.Coding/BashGuard.cs", "docs/guide.md"])

        self.assertEqual(0, result.returncode)


if __name__ == "__main__":
    unittest.main()
