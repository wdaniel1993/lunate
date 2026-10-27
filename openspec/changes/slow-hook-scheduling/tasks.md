## 1. The regression test first

- [x] 1.1 `tests/Lunate.Extensibility.Tests`: a `PoolStarvation` harness that loads the pool the way a
      shared CI runner is loaded — every worker blocked on a gate, work queued at the pool's global
      queue (queued from a thread of its own, since a pool worker's queue is only stolen from when it
      is idle), and a keeper that piles more on top once the dispatch has queued its first
      continuation — released in a `using`.
- [x] 1.2 `A_hook_the_starved_pool_delayed_is_not_reported_slow`: a handler whose own work is nothing
      (`await Task.Yield()`, no allocation, no I/O) is not called slow, does not feed the maximum or
      the slow count, and raises no warning and no notice, while the dispatch is still counted; run it
      first and watch it fail.
- [x] 1.3 `A_dismissed_crossing_does_not_spend_the_warning`: the same hook, the same handler, on a pool
      that has caught up, still warns once — the latch a dismissal left unset is spent by the real
      crossing, and the hook's maximum is the handler's own time.

## 2. The verdict

- [x] 2.1 `HookRunner.Timings`: a crossing is reported only when the pool is not behind — more work
      waiting for the pool than it has workers (`PendingWorkItemCount` against `ThreadCount`), read
      once when the dispatch settles.
- [x] 2.2 A crossing the loaded pool explains is counted as a call in the totals, the last value and
      the histogram, and does not feed the maximum, the slow count or the warning; the warn-once latch
      stays unset.
- [x] 2.3 Cancelled dispatches keep their current, unjudged treatment.
- [x] 2.4 No public API change: `PublicAPI.Unshipped.txt` untouched, `HookRunnerOptions` unchanged,
      no allocation and no await added to the dispatch path.

## 3. Documentation

- [x] 3.1 `docs/extensions/contributions/hooks.md`: say what the slow-hook number is and that a
      crossing the loaded pool explains is not reported.
- [x] 3.2 `docs/spec/extensibility.md`: the same, in the extensibility summary's hook sentence.
- [x] 3.3 `CHANGELOG.md`: record the fix under the next release.

## 4. Gate

- [x] 4.1 `dotnet csharpier format .` (never hand-format), then `dotnet csharpier check .`.
- [x] 4.2 `openspec validate slow-hook-scheduling --strict`.
- [x] 4.3 Release build, then the hook-timing suites: `Lunate.Extensibility.Tests` (602/0, including
      the new starved cases five runs in a row), `Lunate.Extensibility.Remote.Tests` (77/0),
      `Lunate.Coding.Tests` slow-hook notice tests (3/0).
- [x] 4.4 `bash scripts/verify.sh --profile docs` for the prose, and the full gate on the branch.
