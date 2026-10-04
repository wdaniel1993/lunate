# S-3 — Startup baselines and budget calibration

Question: what are the per-environment startup baselines (dev machine and each
CI runner), and what budgets follow from them with documented headroom?

- Calibration: done 2026-10-04 — see [report.md](report.md).
- Results: CI budgets 250 ms (Ubuntu), 300 ms (macOS), 350 ms (Windows);
  local default stays 150 ms.
- Evidence: `evidence/` (raw `gh run view` medians, run list, local hyperfine
  JSON).
- ADR draft: [`adr/0005-budget-calibration.md`](../../../adr/0005-budget-calibration.md)
  (proposed; includes the repeatable re-calibration procedure).
