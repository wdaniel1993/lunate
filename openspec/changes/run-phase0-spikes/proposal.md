## Why

The architecture's load-bearing assumptions are still unvalidated, and Phase 1–2 code will be built on top of them. This change (guide card T-03) runs the four Phase 0 spikes so each assumption is proven or refuted with evidence before it hardens into the codebase: the loop (own vs Microsoft Agent Framework harness — S-1), raw-key input under Git Bash/mintty (S-2), startup baselines and budget calibration (S-3), and in-process Roslyn loading (S-4, the C# differentiator).

## What Changes

- Spike code and reports under `docs/spikes/S-1..S-4/` (throwaway projects, outside the solution and the verify gate).
- **S-1**: minimal MAF-harness agent vs a minimal own-loop agent, both with our four tools and optional features off; six checks (startup/memory, prompt size, event stream incl. steering and cancel, approvals, replay determinism, API churn).
- **S-2**: raw-key reading spike for a .NET app inside mintty (Git Bash); report with the supported/fallback decision.
- **S-3**: startup baselines measured across CI runners and the dev machine; calibrated budgets written into `scripts/perf.sh` and the CI matrix; calibration discipline documented.
- **S-4**: `MSBuildLocator` + `MSBuildWorkspace` load of a fixture solution and one large real solution, including from the published single-file build; latency and failure-mode findings.
- One proposed durable ADR per spike (loop choice, mintty support, budget calibration, Roslyn placement), each requiring maintainer sign-off before adoption.

## Capabilities

### New Capabilities
- None — spikes produce knowledge, ADRs and calibrated values; they do not change system behaviour.

### Modified Capabilities
- `repo-foundation`: the startup budget becomes a calibrated, per-environment value with a re-calibration discipline (S-3), replacing the placeholder constants.

## Impact

- `docs/spikes/` (new), `adr/` (new ADRs 0003+), `scripts/perf.sh` default budget, `.github/workflows/ci.yml` matrix budgets.
- No product code; no changes to `src/`; spike projects are throwaway and not part of `lunate.sln`.
