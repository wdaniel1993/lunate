import subprocess
import sys
import unittest
from pathlib import Path


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
CHECKER = REPOSITORY_ROOT / "scripts" / "check-adr-numbers.py"
FIXTURES = REPOSITORY_ROOT / "tests" / "fixtures" / "adr-numbering"


class AdrNumberingTests(unittest.TestCase):
    def run_checker(self, fixture: str) -> subprocess.CompletedProcess[str]:
        return subprocess.run(
            [
                sys.executable,
                str(CHECKER),
                "--directory",
                str(FIXTURES / fixture / "adr"),
            ],
            capture_output=True,
            text=True,
            check=False,
        )

    def test_duplicate_numbers_report_both_files_and_next_free_number(self):
        result = self.run_checker("duplicate")

        self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
        output = result.stdout + result.stderr
        self.assertIn("tests/fixtures/adr-numbering/duplicate/adr/0001-first.md", output)
        self.assertIn("tests/fixtures/adr-numbering/duplicate/adr/0001-second.md", output)
        self.assertIn("next free number: 0003", output)

    def test_gap_reports_bracketing_files_missing_number_and_next_free_number(self):
        result = self.run_checker("gap")

        self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
        output = result.stdout + result.stderr
        self.assertIn("tests/fixtures/adr-numbering/gap/adr/0001-first.md", output)
        self.assertIn("tests/fixtures/adr-numbering/gap/adr/0003-third.md", output)
        self.assertIn("missing ADR-0002", output)
        self.assertIn("next free number: 0004", output)

    def test_clean_consecutive_numbers_pass(self):
        result = self.run_checker("clean")

        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)


if __name__ == "__main__":
    unittest.main()
