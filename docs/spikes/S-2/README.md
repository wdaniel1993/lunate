# S-2 — Git Bash (mintty) input

Question: does raw-key reading work for a .NET console app inside mintty (Git
Bash), and if not, what is the fallback?

- Spike: `RawKeys/` (throwaway console app, not in `lunate.sln`).
- Automated checks: done 2026-10-04 — see [report.md](report.md).
- Manual mintty check on Windows: **awaiting the maintainer run** (procedure in
  `report.md`, evidence to be captured under `evidence/`).
- ADR draft: [`adr/0004-mintty-input-support.md`](../../../adr/0004-mintty-input-support.md)
  (proposed; Windows Terminal fallback included).
