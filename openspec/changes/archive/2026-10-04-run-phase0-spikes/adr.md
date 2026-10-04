# ADR Review Manifest

- Status: completed — finalized at archive, 2026-10-04 (all four ADRs accepted)
- Review date: 2026-10-04

## Review Summary

ADR review completed for this change. The change is designed to PRODUCE durable ADRs from spike results; this manifest lists the ADRs and was finalized at archive time (2026-10-04) — all four files exist and are accepted. In-force ADRs 0001–0002 were reviewed and constrain the spike methods (single-file release shape; layering and model types).

## In-Force ADRs Reviewed

- `adr/0001-no-aot-single-file-budgets.md` — S-3 calibrates the budgets this ADR relies on; S-4 tests the single-file constraint against Roslyn.
- `adr/0002-layering-and-meai-model-types.md` — S-1 tests the loop boundary these layers assume.

## New Durable ADRs Created

- Created and **accepted** (maintainer sign-off, 2026-10-04):
  - `adr/0003-loop-own-vs-maf-harness.md` (S-1) — accepted with the tool-exception consequence
  - `adr/0004-mintty-input-support.md` (S-2) — accepted after the manual mintty run (ReadKey works under ConPTY; 15/15 keys)
  - `adr/0005-budget-calibration.md` (S-3) — accepted
  - `adr/0006-roslyn-placement.md` (S-4) — accepted
