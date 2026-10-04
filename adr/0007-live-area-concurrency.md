# 0007 — TUI live-area concurrency: System.Reactive at the boundary

- Status: proposed — awaiting maintainer sign-off
- Date: 2026-10-04
- Spike: `docs/spikes/S-5/report.md` (raw evidence under `docs/spikes/S-5/evidence/`)
- Relates to: ADR 0002 (layering and Microsoft.Extensions.AI model types), ADR 0001 (single-file releases), ADR 0005 (startup budgets)

## Context

The guide leaves the live-area concurrency model open. `Lunate.Agent`'s contract
is fixed at `IAsyncEnumerable<AgentEvent>`; the live area must merge that stream
with keyboard, resize and a frame clock, cap tail redraws at ~30 fps, animate a
spinner while a tool runs, show a footer and an approval prompt, queue
steering, cancel on `Esc` and handle `Ctrl+C` clear/double-quit. T-18/T-19 will
harden whichever model this ADR picks.

S-5 implemented the same scenario four ways — baseline (no concurrency), A
(plain async `Channel<T>` + periodic `TimeProvider` timer), B (System.Reactive,
`Scan` + `Sample` + `TestScheduler`) and B+ (B plus ReactiveUI 26 view models) —
and measured startup, memory, assemblies, LoC, a shared test list (50 runs,
2400 executions, 0 flakes), a 10,000-delta burst, readability and dependency
health. Findings that drive this decision:

1. B's cost is acceptable: +10 ms whole-process startup and +7.6 ms internal
   first-frame versus baseline (24 → 34 ms; design guidance: under ~20 ms), +7 MB
   RSS, +2 assemblies (System.Reactive + test scheduler).
2. B's time-based tests are the simplest: `TestScheduler` controls every clock
   in tests without a wall clock, timer or drain barrier. A's equivalent needed
   a rendezvous protocol because `PeriodicTimer.WaitForNextTickAsync` races
   `FakeTimeProvider.Advance` (reproduced `AccessViolationException`); A had to
   fall back to `TimeProvider.CreateTimer`.
3. B is the smallest plumbing (114 LoC vs A 164, B+ 298) and the readability
   review ranked A easiest, B middle, B+ hardest.
4. B+ (ReactiveUI) does not pay for itself: 13 extra assemblies, ~4-day release
   cadence, its own reactive primitives instead of System.Reactive, duplicated
   footer/approval state, render paths that bypass the 30 fps `Sample` cap, and
   a key handler that re-enters the pipeline through a `ReactiveCommand`. Its
   startup overhead is small (~1.7 ms internal), but the complexity moved
   rather than disappeared.
5. Packaging risk: with the exact verify publish flags (self-contained,
   single-file, ReadyToRun, compressed) the B+ binary crashed
   `AccessViolationException` in 11/100 starts and A in 1/100; baseline and B
   were stable (0/100). Root cause not isolated in the timebox; see Follow-ups.

## Decision (proposed)

- **Adopt System.Reactive for the `Lunate.Tui` live area only.** The agent
  contract stays `IAsyncEnumerable<AgentEvent>`; the TUI converts at the
  boundary (`.ToObservable()` / `await foreach` producers) and the live area is
  a merged observable pipeline.
- **Inject the scheduler; never use wall-clock time operators directly.**
  Production uses a real scheduler (default or a dedicated event-loop
  scheduler); tests use `TestScheduler` for everything time-based (`Sample`,
  `Throttle`, `Interval`, debounce, the `Ctrl+C` window).
- **Cap redraws with `Sample(FrameInterval)`** (~30 fps) and drive the spinner
  from the same sampled pulse; keep one render function that paints the live
  area from a snapshot.
- **Do not adopt ReactiveUI for the live area now.** Status footer and approval
  prompt stay plain state rendered by the render function. Re-open only if a
  later UI grows enough that view models demonstrably remove complexity.
- **Pins for `Lunate.Tui`:** `System.Reactive` 7.0.0 and
  `Microsoft.Reactive.Testing` 7.0.0 (MIT); a new package dependency needs the
  normal maintainer approval and a `PublicAPI.Unshipped.txt` entry is not
  expected (Rx types are not part of Lunate's public API).
- **Keep plain async where there is no time dimension** (agent loop, tools,
  protocols): Rx is a TUI-boundary tool, not a project-wide style.
- **Startup budget:** the live area adds ~10 ms whole-process; ADR 0005's
  local budget (150 ms) is unaffected. Record the delta in the TUI change.

## Alternatives considered

- **A — plain async `Channel<T>` + periodic timer:** viable and easiest to read
  (one queue, one consumer, one render gate), but time-based testing needs a
  bespoke drain/rendezvous contract and a testable timer API; the naive
  `PeriodicTimer` + `FakeTimeProvider` combination is a data race. Rejected as
  the primary model; its synchronous channel loop remains the right pattern for
  non-TUI concurrency.
- **B+ — B plus ReactiveUI view models:** rejected for now (see finding 4).
  Revisit only with evidence that view models remove complexity, and only after
  the packaging crash is understood.
- **Keep the spike's shared mutable state machine:** it made the four variants
  comparable and does not survive as-is; T-18 should model immutable state
  snapshots or a single-threaded owner, as the readability review notes.
- **`FunctionInvokingChatClient` / MAF harness:** out of scope here; ADR 0003
  already rejected both for the loop.

## Consequences

- `Lunate.Tui` takes a dependency on `System.Reactive` (and the test package);
  `Lunate.Agent`/`Lunate.Ai`/`Lunate.Protocols`/`Lunate.Coding` do not.
- T-18/T-19 must build the live area with an injected `IScheduler` and cover the
  time-based behaviours with `TestScheduler`; no `Thread.Sleep`-based TUI tests.
- Technical readouts (footer percent, token counts) format with invariant
  culture; TUI tests must not depend on the machine locale (found in S-5
  verification: `12,5%` under a German locale).
- The live-area render path must stay single-writer; the spike's cross-thread
  reads with an implicit drain contract are explicitly not the production shape.
- Follow-up work (separate change, not this spike):
  1. Reproduce and root-cause the single-file + ReadyToRun + compression
     `AccessViolationException` (A 1/100, B+ 11/100) on all three RIDs, and
     re-check whether adding `System.Reactive` drags the risk into B.
  2. Re-run the TUI startup delta with the real live area once T-18 lands, to
     replace B's synthetic +10 ms with a product number.
- Re-open this ADR if System.Reactive's maintenance cadence changes, if
  `TestScheduler` cannot model a required behaviour, or if B+ is revisited.
