## Context

S-5 decides the concurrency model for the TUI live area before T-18/T-19 implement it. `Lunate.Agent`'s contract is fixed at `IAsyncEnumerable<AgentEvent>`; any Rx conversion happens only at the `Lunate.Tui` boundary. Same inline design as the guide: Spectre for finished blocks, own live area at the bottom, no Terminal.Gui, no full-screen UI. Timebox: one day. Spike code is throwaway and does not run in the verify gate.

## Goals / Non-Goals

**Goals:**
- A decision (A/B/B+) grounded in measured startup cost, testability of time-based behaviour, LoC and readability.
- A check matrix like S-1/S-4, with evidence files, surprises and limits.
- One proposed ADR (0007) ready for maintainer sign-off.

**Non-Goals:**
- A production live-area implementation (that is T-18/T-19).
- ReactiveUI view bindings or an MVVM architecture — B+ uses view models only, subscribed by a render function.
- Product package decisions: Rx packages are candidate dependencies for `Lunate.Tui`; the report records them and adoption needs the ADR sign-off.

## Decisions

- **Variants**: A plain async — a `Channel<T>` merge of event/keyboard/resize sources with a `PeriodicTimer` frame clock; B — System.Reactive merged observables with `Sample`/`Throttle` and `TestScheduler` for time in tests; B+ — B plus ReactiveUI view models for the status footer and approval prompt only.
- **Scenario (identical across variants)**: inputs are replayed `AgentEvent`s (recorded fixture or scripted), a scripted key sequence via a fake console, one resize event and a spinner timer. Behaviour: (1) streaming text tail, redraw capped at ~30 fps; (2) spinner while a tool runs + status footer (model, tokens, context %); (3) approval prompt yes/no/always-for-session; (4) steering text typed during a run is queued and shown as queued; (5) `Esc` cancels the run; `Ctrl+C` clears the input, twice within 2 s quits.
- **Measurements**: (1) startup delta and idle memory vs a baseline, single-file ReadyToRun build (same method as S-3); number of added assemblies; (2) lines of code per variant, excluding tests; (3) one shared test list for all variants including the time-based tests (spinner frame, throttling, Ctrl+C window), each suite run 50×, reporting flaky tests; (4) 10,000-delta burst — no lost or reordered events, cancel leaves a clean state; (5) readability — the reviewer subagent explains each variant's flow in five sentences and lists what was hard to follow; (6) dependency health — versions, licences, release cadence, ReactiveUI initialisation cost (Splat).
- **Decision guidance**: prefer B if the time-based tests are clearly simpler and the startup delta stays under ~20 ms; choose B+ only if the view models remove real complexity, not just move it.
- **Package pinning**: every spike package pinned to an exact version and recorded in the report; spike packages are exempt from the product dependency rule.

## Risks / Trade-offs

- [Spike favouritism] → all variants implement the same scenario and the same test list; the reviewer reads all three.
- [Startup measurements on a dev machine only] → same caveat as S-3/S-4; the delta is the signal, not absolutes.
- [Rx adoption would add dependencies to `Lunate.Tui`] → the report records assembly count and licence/cadence; the ADR decides with maintainer sign-off.

## Migration Plan

Not applicable — additive spike work.

## Open Questions

- None blocking; the ADR answers the model choice.
