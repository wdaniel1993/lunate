# S-5 readability review (adversarial-reviewer agent)

Reviewer: `adversarial-reviewer` subagent, 2026-10-04. Reviewed revision: the
group-1 commit plus the `CreateTimer` change in variant A (before the
one-line apply/render clarity edit noted in the council notes). The prompt asked
for exactly five sentences per variant and a concrete hard-to-follow list.

## 1. Variant flows

### Variant A — plain async

1. Agent events, keys, resizes and frame ticks are all written as `LiveInput`s into one unbounded `Channel<LiveInput>`, with the frames produced by a `FakeTimeProvider.CreateTimer` callback, and a single `Task.Run` consumer loop drains them FIFO through `Apply`.
2. All state lives in one shared mutable `LiveAreaState` that only `Apply` mutates, while public properties and `Snapshot()` read that same object from the caller's thread.
3. The 30fps cap is emergent: `Apply` renders only when the input is a `LiveInput.Frame` and `LiveAreaState.Apply` returns true, so events, keys and resizes merely set `Dirty` and wait for the next timer tick.
4. The spinner glyph uses `_state.FrameNumber`, which `LiveAreaState.Apply` increments on every `Frame` input even when that frame produces no render.
5. Esc and double-Ctrl+C are queued like any other key and observed only after the loop applies them, and `DrainAsync` writes a sentinel plus completion source after the producers so the barrier is FIFO as long as channel writes and timer callbacks happen on the calling thread.

### Variant B — System.Reactive

1. Events, keys and resizes go into one `Subject<LiveInput>` and are folded by `Scan(_state, …)` into the same mutable `LiveAreaState` instance, so the "state stream" emits that one shared object rather than snapshots.
2. `Replay(1).RefCount()` plus a no-op `Subscribe(_ => { })` keeps the fold alive, but the session's properties and `Snapshot()` still read `_state` directly.
3. The pulse merges state changes with `Interval(FrameInterval)` filtered on `_state.ToolRunning`/`PendingApprovalText` and passes the result through `Sample(FrameInterval)`, whose callback then applies `new LiveInput.Frame()` directly to `_state`, bypassing the subject and `Scan`, and renders when that returns true.
4. The spinner is `Spinner.Glyph(_state.ToolRunning, _state.FrameNumber)`, and `FrameNumber` advances only inside that direct `Frame` application, so animation is driven by the sampled pulse.
5. Esc and double-Ctrl+C travel as `LiveInput.Key` through the subject and are applied inside `Scan`, while `DrainAsync` is a no-op because `TestScheduler`/`Subject` delivery happens synchronously on the caller.

### Variant B+ — B plus ReactiveUI view models

1. It reuses B's subject → `Scan(_state)` → `Replay/RefCount` fold, but subscribes `SyncViewModels` to copy the shared state into `StatusFooterViewModel` and `ApprovalViewModel` on every input.
2. In `Key`, approval keys are resolved by `ApprovalViewModel.TryResolve`, which executes a `ReactiveCommand` whose callback pushes `LiveInput.Approval` back into the subject synchronously, re-entering the pipeline from inside a key handler, and only unclaimed keys are forwarded as `LiveInput.Key`.
3. Rendering has three triggers: the sampled `FrameInterval` pulse plus `Changed` notifications filtered to `Text` and `Prompt`, and each `RequestRender` applies a `Frame` directly to `_state` outside the `Sample` cap.
4. `Snapshot` overlays `_footer.Text` and `_approval.Prompt` onto `_state.Capture(...)`, so footer and approval state exist in two places and stay aligned only because `SyncViewModels` runs on every emission.
5. Cancellation and quit are Esc and double-Ctrl+C keys that still flow through the subject into `_state`, `DrainAsync` is again a no-op, and a single `Usage` event can raise `Text` several times and thus render several times in the same instant while a tool runs.

## 2. Hard to follow

- `S5.VariantA/VariantASession.cs:38-42,70` — determinism depends on `FakeTimeProvider` invoking the `CreateTimer` callback synchronously on the `Advance` caller and on channel FIFO before the `Drain` sentinel; this load-bearing ordering exists only in a comment.
- `S5.VariantA/VariantASession.cs:140` — `if (_state.Apply(input) && input is LiveInput.Frame)` applies state before type-checking; it reads as "changed and frame" but means "always apply, render only frames", so redraw-needing non-frame inputs leave `Dirty` for later with no visible signal.
- `S5.VariantA/VariantASession.cs:43,48-56,83-86` — `_state` is mutated on a thread-pool thread while properties and `Snapshot` read it unsynchronized; correctness rests on callers calling `DrainAsync` first, an unwritten `ISession` contract (polling `IsRunning` early is a data race).
- `S5.VariantASession.cs:72-81` — `DrainAsync` has two outcomes (sentinel processed vs `TryWrite` false) plus a 10s `WaitAsync` that throws, so failure behaviour depends on channel lifecycle callers cannot observe.
- `S5.VariantASession.cs:88-106` — shutdown mixes `_cts.Cancel()`, `TryComplete()`, blocking `_loop.Wait(1s)` and a swallowed `AggregateException`; which mechanism ends the loop, and whether a pending drain ever completes, is ambiguous.
- `S5.VariantASession.cs:25-26,60,67` — unbounded channel with ignored `TryWrite` results means the "30fps cap" is not an enforced gate but an artifact of drain-loop coalescing; queued frames can render back-to-back in a burst.
- `S5.VariantB/VariantBSession.cs:30-37` — `Scan` folds into one mutable `_state` and `Replay(1)` caches that reference; the pipeline looks like a stream of states but every emission is the same object being mutated.
- `S5.VariantBSession.cs:39` — the empty `Subscribe(_ => { })` is used as a `RefCount` lifetime pin; it is likely redundant because `pulse` (line 52) also subscribes, so the intended lifetime rule is unstated.
- `S5.VariantBSession.cs:47-50` — the interval `Where` reads mutable `_state`, and `Interval(FrameInterval)` plus `Sample(FrameInterval)` land on the same virtual tick, so emission depends on scheduler ordering and a tick can be deferred a full period.
- `S5.VariantBSession.cs:52-58` — the pulse callback applies `new LiveInput.Frame()` directly to `_state`, bypassing `_inputs`/`Scan`; the class comment's "folded" story is false and frames are invisible to the state stream.
- `S5.VariantBSession.cs:54,87` — rendering mutates `FrameNumber` (spinner phase) and `DrainAsync` is a no-op; post-`Post` reads are safe only because `Subject` is synchronous, unlike A's explicit barrier.
- `S5.VariantBPlus/VariantBPlusSession.cs:31,83-89` — `Key` → `TryResolve` → `ReactiveCommand` → `OnNext(Approval)` → `Scan` → `SyncViewModels` → `Changed` → `RequestRender` → `Apply(Frame)` → render runs nested inside a key call, giving two key paths and implicit ordering.
- `S5.VariantBPlusSession.cs:48-53` with `S5.VariantBPlus/StatusFooterViewModel.cs:13-71` — redraw hangs on a manually raised computed `Text` property; each setter double-checks equality, then `RaiseAndSetIfChanged`, then manually raises `Text`, so a new field or forgotten raise silently disables rendering.
- `S5.VariantBPlusSession.cs:136-142` — `RequestRender` bypasses `Sample`: multiple `Text` raises per event (for example `Usage` while a tool runs) each apply a frame and render, so the 30fps cap covers only the pulse path.
- `S5.VariantBPlusSession.cs:101-108,127-134` — footer and approval data are duplicated between `LiveAreaState` (`LiveAreaState.cs:84-102`) and the view models and aligned only by `SyncViewModels`, leaving the state's own footer/prompt values dead.
- `S5.VariantBPlus/ApprovalViewModel.cs:35-52` vs `LiveAreaState.cs:213-224` — the y/n/a mapping and prompt formatting (`ApprovalViewModel.cs:54-57` vs `LiveAreaState.cs:102`) are implemented twice, and `Subscribe(command.Execute())` discards the disposable so decision ordering and failures are invisible.

## 3. Comparative verdict

Variant A is easiest to follow and B+ hardest: A has one input queue, one consumer and an explicit render-only-on-`Frame` gate, while B and especially B+ split mutation between `Scan` and direct `Frame` applications and add re-entrant view-model-driven renders.
