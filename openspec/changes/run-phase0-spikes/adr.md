# ADR Review Manifest

- Status: completed (manifest; durable ADR files are produced during apply, per the plan below)
- Review date: 2026-10-04

## Review Summary

ADR review completed for this change. The change is designed to PRODUCE durable ADRs from spike results; this manifest lists the planned ADRs and will be finalized at archive time once the ADR files exist and the maintainer has signed off on each. In-force ADRs 0001–0002 were reviewed and constrain the spike methods (single-file release shape; layering and model types).

## In-Force ADRs Reviewed

- `adr/0001-no-aot-single-file-budgets.md` — S-3 calibrates the budgets this ADR relies on; S-4 tests the single-file constraint against Roslyn.
- `adr/0002-layering-and-meai-model-types.md` — S-1 tests the loop boundary these layers assume.

## New Durable ADRs Created

- Planned (created during apply from spike results; maintainer sign-off required before adoption):
  - `adr/0003-loop-own-vs-maf-harness.md` (S-1)
  - `adr/0004-mintty-input-support.md` (S-2)
  - `adr/0005-budget-calibration.md` (S-3)
  - `adr/0006-roslyn-placement.md` (S-4)
