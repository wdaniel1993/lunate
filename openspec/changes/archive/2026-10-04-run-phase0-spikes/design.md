## Context

Phase 0's spikes decide four things that everything else depends on. The maintainer's stated lean is toward the Microsoft Agent Framework harness for the loop; S-1 must test that path fairly, not confirm anyone's prior. Spike code is throwaway: it lives in `docs/spikes/`, is not part of `lunate.sln`, and does not run in the verify gate. Each spike ends with a short report and a proposed durable ADR; adoption decisions are the maintainer's sign-off, not the spike's.

## Goals / Non-Goals

**Goals:**
- Evidence for or against each load-bearing assumption, before Phase 1–2 code depends on it.
- Calibrated, documented performance budgets (S-3) replacing the placeholder constants.
- One proposed ADR per spike, ready for maintainer sign-off.

**Non-Goals:**
- Production code: no `src/` changes; spike projects are not referenced by the solution.
- Live-provider contract testing (spikes use stub chat clients and local measurements).
- Budgets for components that do not exist yet (the TUI idle-memory budget lands with T-18).

## Decisions

- **Spike shape**: one folder per spike under `docs/spikes/S-x/` containing the throwaway project, a `report.md`, and a proposed `adr-draft.md`. Spike projects target net10.0, live outside `lunate.sln`, and are excluded from the gates; NuGet packages used by spikes (including the MAF harness packages) are throwaway and exempt from the product dependency rule.
- **S-1 method**: both minimal agents (MAF harness and own loop) are driven by the same stub `IChatClient` that streams canned responses including a tool call, so the six checks run without API keys. Startup/idle memory are measured on the real processes; prompt size is counted from the assembled system prompt; the event stream (incl. steering and cancel) is exercised through the stub; replay determinism is checked by recording and replaying the stub stream; API churn is a written review of the harness's release history. Outcome options: own loop / harness / borrow parts — the default stays own loop unless checks 2–5 pass cleanly.
- **S-2 method**: a tiny console app that reads raw keys and reports what it sees; automated behavior under redirected input plus a documented manual check in a real Git Bash (mintty) window on Windows (the maintainer's machine or a VM — CI runners have no interactive TTY). Outcome: supported, or a documented fallback (Windows Terminal profile).
- **S-3 method**: use the pipeline as the instrument — the CI matrix already measures startup per OS; dispatch the workflow several times, collect medians per runner, add roughly 50% headroom, round, and write the values into `scripts/perf.sh` (local default) and the CI matrix. The report documents the methodology so re-calibration is a repeatable procedure.
- **S-4 method**: `MSBuildLocator` + `MSBuildWorkspace` in a console spike; fixture solution (console app + library + test project) plus one large real OSS solution (chosen and pinned by commit in the report); measure cold load, edit-to-diagnostics latency, and behaviour with missing restore or broken references; repeat from a published single-file build. Outcome: go / out-of-process / narrow the C# claim.
- **ADR flow**: each spike's result is proposed as a durable ADR (0003+); the maintainer signs off before adoption; the change's `adr.md` manifest is finalized at archive time once the ADR files exist.

## Risks / Trade-offs

- [Spike projects could creep into the solution or gates] → they are excluded by design; the architecture test only inspects `src/`.
- [S-1's stub client under-exercises real streaming quirks] → the stub replays realistic chunked streams including split tool arguments; residual risk noted in the report; a live smoke can follow once Phase 1 lands.
- [S-2 cannot be fully automated] → the manual check is an explicit task with a documented procedure; a fallback is documented regardless of outcome.
- [S-4's large-solution choice affects measurements] → the solution is pinned by commit in the report; conclusions rest on relative behaviour, not absolute numbers.

## Migration Plan

Not applicable — additive spike work; the only production-facing edits are budget values (S-3) and their documentation.

## Open Questions

- Which large OSS solution S-4 uses (decided in the spike, pinned by commit).
- Where the mintty manual check runs (maintainer's machine or a VM — scheduling, not technical).
