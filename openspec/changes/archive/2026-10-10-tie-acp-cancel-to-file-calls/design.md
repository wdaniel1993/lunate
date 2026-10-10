# Design: Tie the ACP cancel to file round trips

## Structure

- `src/Lunate.Protocols/Acp/SessionRunState.cs` (new, internal): the session's run state — one gate, the active run's `CancellationTokenSource`, the prompt count and the pending-cancel latch. Methods: `BeginPrompt`/`EndPrompt`/`BeginRun`/`EndRun`/`Cancel` (moved verbatim from `Session`) plus `CancellationToken Token` (the active run's token, `CancellationToken.None` when idle). One lock: "cancel if active, else latch when a prompt is queued" stays atomic against `BeginRun`, exactly as today.
- `src/Lunate.Protocols/Acp/LibAcpServer.cs`: `session/new` creates the `SessionRunState` **before** the context; `ClientTextFileAccess` receives it; the `Session` class keeps only id/harness/cwd and delegates its run-state calls to the holder (public surface of `Session` unchanged).
- `src/Lunate.Protocols/Acp/ClientTextFileAccess.cs`: constructor gains the `SessionRunState`; `Read`/`Write` pass `state.Token` into the LibAcp calls (replacing `CancellationToken.None`); `Await` gains the fault-observation continuation on the timeout path.

## Pins

- **Cancel semantics**: `session/cancel` → the active run's CTS cancels → the in-flight `fs` request is cancelled with it → `OperationCanceledException` propagates through the sync bridge → the harness rethrows it when the run token is cancelled (`AgentHarness.Tools.cs`: `catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }`) → the prompt response reports the cancellation. No new exception mapping in the bridge: cancellation is not an I/O failure.
- **No behaviour change when idle**: `state.Token` is `CancellationToken.None` outside runs; the timeout bound (30 s default) is untouched; the latch semantics are moved, not modified.
- **Observation**: on `TimeoutException`, attach `request.ContinueWith(static t => _ = t.Exception, OnlyOnFaulted | ExecuteSynchronously)` before throwing the `IOException` — the abandoned request's late fault is observed; a late cancellation needs no observation.
- **Approver**: `ClientApprover` already receives the run token through `IToolApprover.ApproveAsync(…, ct)` and maps cancellation to a decline (T-28 behaviour); its timeout path now also attaches the same fault-only continuation to the abandoned `RequestPermissionAsync` task, so the observation closes for both round-trip kinds.

## Tests (pinned)

- Cancel during a file round trip: a client that does not answer `fs/read_text_file`; a run whose read tool call blocks; `session/cancel`; the prompt response reports cancellation and arrives well before the (injected, longer) timeout — proving the token stopped the call, not the timeout. Assert bounded wall time.
- Observation: after an injected-short timeout abandons a request, let the fake client answer it with an error; force GC and assert no `TaskScheduler.UnobservedTaskException` fires (bounded retries; if the pattern proves flaky on CI, a documented alternative assertion is acceptable — say so in the report).
- Regressions: the existing cancel/latch tests (queued-prompt cancel, cancel mid-stream/mid-tool), the timeout tests, the fs-routing and taxonomy suites stay green unchanged.

## Deviations

- Observation test shape: the pinned "no `TaskScheduler.UnobservedTaskException` fires" assertion proved cross-test-flaky in the full suite — a sibling test abandons a permission request whose connection-closed fault finalizes inside the GC window. That permission timeout path's leak is now fixed by this change (same continuation) and has its own observation test. The file test still flushes finalizable unobserved faults before hooking and counts only events whose exception chain carries the abandoned request's own late error; that scoping stays as a robustness measure, not an out-of-scope carve-out. The red run (continuation absent) fails with exactly that event, so the coverage still proves the fault is observed.
- The observation continuation is attached as `_ = request.ContinueWith(...)` (the returned task discarded) instead of a statement expression, to stay clean under the analyzer/style gate; semantics are exactly as pinned.

## Seams

- The bridge still blocks a thread up to the timeout in the worst case (a client that answers neither the request nor the cancel); the run's cancellation is the primary escape, the timeout the backstop.
- `SessionRunState` is internal (not public API); the seam it serves (`ITextFileAccess`) is unchanged.
