# S-2 — Git Bash (mintty) input

Question: does raw-key reading work for a .NET console app inside mintty (Git
Bash), and if not, what is the fallback?

- Spike: `RawKeys/` (throwaway console app, not in `lunate.sln`).
- Automated checks: done 2026-10-04 — see [report.md](report.md).
- Manual mintty check on Windows: done 2026-10-04 — `Console.ReadKey` works
  directly in mintty on Git for Windows 2.52.0 (pseudo console on by default);
  fails with `MSYS=disable_pcon`. Evidence in `evidence/manual-*.txt`.
- ADR draft: [`adr/0004-mintty-input-support.md`](../../../adr/0004-mintty-input-support.md)
  (proposed; fail-soft diagnostic and Windows Terminal fallback included).
