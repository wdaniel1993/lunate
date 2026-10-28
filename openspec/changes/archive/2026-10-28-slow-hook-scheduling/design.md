## Context

`HookRunner` times every dispatch and reports one warning per (extension, hook) when the first one
crosses `SlowHookThreshold`, plus a per-hook aggregate behind `/extensions`. The measured span is the
dispatch's wall time: from just before the handler is invoked until the runner's own wait has settled.
On an idle machine that number is the handler's; on a loaded one it belongs to the thread pool.

Measured here (macOS arm64, Release, .NET 10, threshold 20 ms, dispatch driven from a thread of its
own so the measurement is not what is starved, pool occupied by blocked work items):

| handler | reported | the handler's own task |
| --- | --- | --- |
| synchronous (`ValueTask.FromResult`) | 0.94 ms | 0.09 ms |
| `await Task.Yield()` | 648–1 392 ms | 648–1 392 ms |
| `await Task.Delay(1)` | 9.5 ms | 3.4 ms |
| `await Task.Delay(20)` | 159 ms | 164 ms |

Three facts follow, and they decided the design:

1. The runner's own observation is not where the time goes — `Task.WhenAny` and the resumption after
   it cost under a millisecond — so a timestamp stamped on the handler's completion changes nothing.
2. The inflation happens before the handler's task completes, inside its own continuations, where no
   clock the runner starts can see it.
3. A clock the runner *could* start to measure the pool is itself queued on the worker that asks:
   measured here, a `Task.Run` round trip taken from the verdict returned in 2 µs while 200 work items
   were queued, because the runner's thread runs its own local queue first. Sampling the pool with
   work is not available from inside the pool.

## Goals / Non-Goals

**Goals**

- A hook that was never slow is not reported slow, however loaded the machine is.
- A hook that is genuinely slow is still reported, and still reported once.
- The check costs nothing on the dispatches that do not cross the threshold, and nothing measurable
  on the ones that do.
- A test that fails without the fix: under artificial thread-pool load a fast handler is not reported.

**Non-Goals**

- Reconstructing the handler's true execution time. For an in-process handler the runner owns neither
  its continuations nor a clock inside it; a number derived from outside is a guess, and inventing one
  would repeat the defect in the other direction.
- Judging a hook whose dispatch was cut off by the hook timeout: a timeout stays the hook's cost, as
  the spec already says, unless the pool explains it too.
- Changing `HookRunnerOptions` or any public API.

## Decisions

**Judge the crossing; do not re-time the handler.** The aggregate keeps the dispatch's measured
elapsed time, which is a true statement about what the dispatch cost. What changes is the verdict:
before calling a crossing slow, the runner asks whether the machine is behind and withholds the
verdict when it is. Re-timing was rejected because the only instrument that could separate a 20 ms
`Task.Delay` from pool queueing lives inside the handler, and an in-process handler is code the runner
does not own; `IPreparationTimeReporter` exists precisely because a *remote* handler's own clock is
the only place a child can report from.

**The witness is the pool's queue, read at the verdict.** While more work waits for the pool than the
pool has workers, a queued continuation — the handler's own included — waits for a thread instead of
running, so a dispatch that took that long there measured the queue. Two counter reads
(`PendingWorkItemCount`, `ThreadCount`), nothing allocated, nothing awaited. Reading it *before* the
handler as well was tried and dropped: a dispatch whose burst has already ended is the rarer case, and
the earlier read made every crossing depend on the pool's state at dispatch time, which is exactly the
state a test cannot hold still.

**Sampling the pool with work was measured and rejected.** A round trip (`Task.Run`, `Task.Yield`) from
inside the runner measures the *calling worker's local queue*, not the pool's backlog: the worker that
asks runs its own item next (2 µs measured with 200 items queued). A bound-and-cap variant, three
samples with the least delayed taken, was built and removed for the same reason — it cannot see what it
was meant to measure, and it added an await to every crossing.

**A crossing the loaded pool explains sits where a cancelled dispatch sits.** It counts as a call in
the totals, the last value and the histogram, and does not feed the maximum, the slow count or the
warning. This reuses a rule the spec already carries ("a cancelled dispatch is counted but never
slow"), so `/extensions` cannot show a maximum that no handler produced, and the warn-once latch stays
unset: the first crossing on an idle machine still warns.

Alternatives considered: raising the threshold (moves the lie, does not remove it); measuring only the
synchronous part of the call until the handler first suspends (would never report a handler that awaits
real work — the normal case for a remote interceptor); subtracting a fixed scheduling estimate per
dispatch (a guess, and one that changes the aggregate for hooks that never crossed); classifying by
`ThreadPool.PendingWorkItemCount` alone at the dispatch's start (the burst may end before the dispatch
does).

## Risks / Trade-offs

- [A loaded machine can hide a genuinely slow handler] -> The warning exists to name a hook the machine
  cannot account for; while the pool is behind, the machine accounts for everything. The aggregate
  still records the dispatch's cost, so `/extensions` and `--json` keep the raw number.
- [The witness is a queue depth, not a measured delay] -> It can only withdraw a verdict, never create
  one, and it is only consulted on a dispatch that already crossed the threshold. A wrong read makes
  a warning quieter, never false.
- [A hook dispatched while the pool is briefly busy is not reported even if it was slow] -> Accepted,
  and the reason the read is one comparison rather than a heuristic: the failure mode the change is
  for (accusing a hook of the scheduler's time) costs more trust than an occasional missing warning.
- [Existing timing tests could be withheld if the test process itself is loaded] -> The test assembly
  runs its classes serially (`Parallelization(Mode = None)`), the existing tests keep a wide ratio of
  handler delay to threshold, and the new test loads the pool deliberately and asserts the withholding.

## Migration Plan

No format or API change: nothing to migrate. Reverting is one source file plus the docs.

## Open Questions

None.
