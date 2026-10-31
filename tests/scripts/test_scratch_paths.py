import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
CHECKER = REPOSITORY_ROOT / "scripts" / "check-scratch-paths.py"


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


class ScratchPathTests(unittest.TestCase):
    def test_ledger_under_superpowers_is_refused(self) -> None:
        result = run_check([".superpowers/sdd/issue-241/progress.md"])

        self.assertEqual(1, result.returncode)
        self.assertIn(".superpowers/sdd/issue-241/progress.md", result.stderr)
        self.assertIn("openspec/changes/<name>/", result.stderr)

    def test_worktree_and_tool_caches_are_refused(self) -> None:
        for path in (".worktrees/issue-1/notes.md", ".opencode/session.json", ".claude/settings.local.json"):
            with self.subTest(path=path):
                self.assertEqual(1, run_check([path]).returncode)

    def test_the_leading_dot_survives_normalisation(self) -> None:
        # `lstrip("./")` would eat the dot and let every scratch directory through; this pins the fix.
        result = run_check(["./.superpowers/notes.md"])

        self.assertEqual(1, result.returncode)
        self.assertIn("agent scratch file", result.stderr)

    def test_a_sibling_with_the_same_prefix_is_allowed(self) -> None:
        result = run_check([".superpowers-notes/plan.md", "docs/superpowers.md"])

        self.assertEqual(0, result.returncode, result.stderr)

    def test_repository_content_is_allowed(self) -> None:
        result = run_check(["openspec/changes/x/proposal.md", "src/Lunate.Agent/AgentHarness.cs"])

        self.assertEqual(0, result.returncode)


if __name__ == "__main__":
    unittest.main()
