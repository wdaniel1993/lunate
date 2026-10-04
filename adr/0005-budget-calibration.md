# 0005 — Startup budgets: median plus 50% headroom, re-calibrated from the pipeline

- Status: accepted — 2026-10-04 (maintainer sign-off)
- Date: 2026-10-04
- Spike: `docs/spikes/S-3/report.md` (raw evidence under `docs/spikes/S-3/evidence/`)

## Context

The startup budgets shipped with the CI pipeline were placeholders
(`budget_ms` 250/250/300 in the matrix, 150 ms local) chosen before any
calibration. Spike S-3 measured the published single-file binary across eight
CI runs (three dispatched for this calibration) and on the dev machine. The
data shows the runners are noisy — macOS startup medians on the same commit
ranged 165–380 ms and two dispatch runs failed the old 250 ms macOS budget —
so budgets must be derived from repeated measurements and re-checked when a
baseline shifts, exactly as the `repo-foundation` spec requires.

## Decision

- **Budget rule**: per environment, take the median of the measured startup
  medians, add roughly 50% headroom, and round **up** to the next 50 ms. The
  rule deliberately rounds up so the budget never falls below the intended
  headroom.
- **Memory budgets** are separate: ADR 0009 extends the same calibration rule
  (median + ~50%, rounded up) to idle, working-set and Roslyn memory.
- **Calibrated values** (2026-10-04): CI matrix `budget_ms` Ubuntu 250,
  macOS 300, Windows 350; local `scripts/perf.sh` default stays 150 ms.
- **Ownership**: budgets live in `.github/workflows/ci.yml` (per-runner via
  `BUDGET_MS`) and `scripts/perf.sh` (local default); `BUDGET_MS` remains the
  override. `scripts/gate-tests.sh` keeps the gate self-tested.
- **Re-calibration discipline**: re-run the procedure in the S-3 report when a
  runner image changes, hardware changes, or a budget shows repeated flakes;
  record the new measurements and the reason in the report before touching the
  values. Calibration is a snapshot, not a one-off.
- **Interpretation**: a budget failure means "investigate a material
  regression (a doubling trips every budget) or a runner-noise spike"; a
  single extreme-runner failure is a re-calibration trigger, not automatically
  a code defect.

## Alternatives considered

- **Keep the placeholder values**: rejected — they were guesses; macOS was
  already flaky at 250 ms and Windows had no measured justification for 300.
- **One generous flat budget (e.g. 500 ms everywhere)**: rejected — hides real
  regressions and erases per-runner baselines.
- **Mean and p95 instead of median plus 50%**: rejected for now — eight runs
  are too few for a stable p95; the median rule is simple and the report
  documents its sensitivity.
- **Drop the startup budget**: rejected — the `repo-foundation` spec requires
  a calibrated budget that fails on a material regression.

## Consequences

- Every CI runner gains headroom over its measured baseline; the local default
  is unchanged at 150 ms so slower dev machines are not gated by this
  machine's 95 ms.
- Budgets remain deliberately loose enough to absorb shared-runner noise; a
  380 ms macOS outlier would still fail even the new budget and must be
  handled by re-calibration, not by silencing the gate.
- Re-calibration is now a documented procedure; the next person repeats the
  measurements instead of re-deriving the rule.
