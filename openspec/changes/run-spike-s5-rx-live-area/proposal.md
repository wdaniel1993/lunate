## Why

The TUI live area (streaming tail, spinner, footer, approval prompt, steering) is where time-based behaviour meets terminal rendering, and the guide leaves the concurrency model open. The maintainer ordered a spike (S-5, timebox one day) to decide between plain async code and Reactive Extensions (optionally plus ReactiveUI view models) before T-18/T-19 harden either choice. The decision is judged on testability of time-based behaviour, startup cost and readability — not preference.

## What Changes

- Spike code and report under `docs/spikes/S-5/` (throwaway, outside `lunate.sln`, excluded from the verify gate): one scenario implemented three times — A: plain async (`Channel<T>` merge, `PeriodicTimer`); B: System.Reactive (merged observables, `Sample`/`Throttle`, `TestScheduler` in tests); B+: B plus ReactiveUI view models (`ReactiveObject`, `ReactiveCommand`) for the status footer and approval prompt, subscribed by a plain render function (no ReactiveUI view bindings).
- The scenario is identical for every variant: replayed `AgentEvent`s, a scripted key sequence through a fake console, one resize event, a spinner timer — streaming tail redraw capped at ~30 fps, spinner + status footer, approval prompt (yes/no/always), steering queue display, `Esc` cancel and `Ctrl+C` clear/double-quit.
- Measurements: startup delta and idle memory vs a baseline (single-file ReadyToRun, S-3 method) and added assemblies; lines of code per variant; the same test list for all variants including time-based tests, each suite run 50× with a flake report; correctness under a 10,000-delta burst; readability review by the reviewer subagent; dependency health (versions, licences, cadence, ReactiveUI/Splat initialisation cost).
- One proposed durable ADR (0007): A, B or B+ — prefer B if the time-based tests are clearly simpler and the startup delta stays under ~20 ms; choose B+ only if the view models remove real complexity.

## Capabilities

### New Capabilities
- None — the spike produces knowledge, a report and a proposed ADR; it does not change system behaviour.

### Modified Capabilities
- None — the live-area design lands with T-18/T-19 once the ADR is signed off.

## Impact

- `docs/spikes/S-5/` (new), `adr/0007-*.md` (proposed draft).
- No product code; no `src/` changes; spike projects are throwaway and not part of `lunate.sln`.
