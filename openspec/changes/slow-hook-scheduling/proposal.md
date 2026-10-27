## Why

The slow-hook warning accuses an extension of time the machine spent: a dispatch is timed with a
wall clock around the handler, so on a loaded thread pool the number belongs to the scheduler. A
reproducer under artificial pool starvation (threshold 20 ms, macOS arm64, Release, .NET 10) recorded
1.4 s for a handler whose own work is nothing (`await Task.Yield()`, no allocation, no I/O), and a
trivial hook crossed a 20 ms threshold every time the pool had work queued behind it. A warning that
names an extension for the scheduler's delay costs the warning its credibility: the extension author
cannot reproduce it, and `/extensions` reports a maximum the hook never had.

Timing better is not enough. The inflation is not `Task.WhenAny` — the runner's own observation adds
under a millisecond (a synchronous handler reported 0.94 ms for 0.09 ms of handler time) — it is the
pool queueing the *handler's own* continuations, which happens before the handler's task completes,
where no clock the runner can read sees it.

## What Changes

- A dispatch that crosses the slow threshold is only *called* slow when the pool is not the reason it
  crossed. The runner reads the one witness a handler's continuations leave behind — the pool's own
  queue — and withholds the verdict while more work waits for the pool than the pool has workers: a
  queued continuation, the handler's included, is then waiting for a thread rather than running.
- A crossing the loaded pool explains sits where a cancelled dispatch already sits: counted as a call
  in the totals, never in the maximum, the slow count or the warning — so a hook that was never slow
  leaves no trace in `/extensions` and no notice in the session, and the warn-once latch stays unset
  for the first crossing on an idle machine.
- Nothing is added to the hot path: the witness is two counter reads, only on a dispatch that already
  crossed the threshold, with no allocation and no await.
- `docs/extensions/contributions/hooks.md` and `docs/spec/extensibility.md` say what the number and
  the verdict are, and `CHANGELOG.md` records the fix.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `extensions`: the slow-hook requirement gains the condition that a crossing is only reported when
  the loaded pool does not explain it, and where such a dispatch sits in the aggregate (counted, never
  the maximum, the slow count, the warning or the latch).

## Impact

- `src/Lunate.Extensibility/HookRunner.Timings.cs`: the verdict and the witness. No public API change
  and no new option: the rule is not configurable.
- `tests/Lunate.Extensibility.Tests`: a harness that loads the pool the way a shared CI runner is
  loaded, and the regression test (a fast handler whose continuation the pool delayed is not
  reported), next to the existing timing tests.
- `docs/extensions/contributions/hooks.md`, `docs/spec/extensibility.md`, `CHANGELOG.md`.
