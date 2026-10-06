## Context

T-10 lands the card (cancel, retries) and repairs the cancellation cluster the full-repo review found. The loop's structure (ADR-0013) does not change: this is semantics inside it. Acceptance per the card: one replay test per path; history stays valid.

## Goals / Non-Goals

**Goals:** every run ends in exactly one terminal event once `RunStarted` was emitted; the history is always valid for the next run; retries are observable and safe; the validator catches the failure shapes this change fixes.

**Non-Goals:** a provider-level tool error flag (deferred: instructing text is the guaranteed channel; revisit with real tools); concurrent runs (still rejected); steering/compaction; changing ADR-0013's structure.

## Decisions

- **Sequential enforcement**: an `Interlocked` guard in `RunAsync`; a second concurrent run throws `InvalidOperationException` naming the sequential rule. The class doc's "later, explicit decision" is now "rejected".
- **Abandonment**: `RunAsync` links a `CancellationTokenSource`; the consumer's stream is read with `CancellationToken.None` so a cancelled user token still delivers the terminal event; the iterator's `finally` cancels the linked source and awaits the loop task. Breaking the enumeration therefore stops the run.
- **Cancellation semantics**: `OperationCanceledException` is user cancellation only `when (ct.IsCancellationRequested)`; everything else is a failure. On cancellation: repair the history, then `RunFinished(cancelled)` (only if `RunStarted` was emitted and no terminal exists). Repair = synthetic error `FunctionResultContent` for every call in the last assistant message without a result — "Tool call (name) was cancelled by the user and was not executed." on cancellation, "Tool call (name) was not executed: the run failed. Do not retry it." on the error path. The in-flight call additionally emits a synthetic `ToolCallResult` event so the event sequence stays valid (not-yet-started calls are message-only); the error-path repair is defensive — with the current execution path tool failures become error results, so pending calls at error time would be an invariant break. Tracked per run because runs are sequential.
- **Retries**: per model-call attempt, `MaxRetries` (default 3) with exponential backoff (`RetryBaseDelay`, default 500 ms; delays run under the token); `Retrying(attempt, reason)` per retry with `attempt` = retry number (1-based). A retry happens only when the attempt emitted no events (text or usage) — a mid-stream failure after content is a run failure, not a silent duplicate. `ProviderErrors` classifies conservatively: timeouts (`TaskCanceledException` not from the caller's token, `TimeoutException`), `HttpRequestException`, `ClientResultException` with status 408/429/500/502/503/504/529, and `AnthropicApiException` with the same statuses; everything else is not retryable. The classifier is public in `Lunate.Ai` (the loop consumes it across the assembly boundary; it needs the SDK compile surface); the guide's design note names it.
- **Stream integrity**: `StreamModelAsync` closes an open text message in a `finally` before any exception escapes; the validator additionally reports messages still open at the terminal event, tool calls not at `Completed`, and events whose run id differs from `RunStarted`'s.
- **Finish reasons and usage**: the last turn's `ChatFinishReason` maps `Length` → `StopReasons.Length` (new), anything else → `stop` (the spec's "richer mapping" clause, now defined; no warning event — the vocabulary is the channel). `UsageContent` emits `UsageUpdated` as it arrives.
- **Small items**: options validated at harness construction (`MaxSteps >= 1`, `MaxRetries >= 0`, `RetryBaseDelay >= TimeSpan.Zero`); null call ids dedupe by instance reference instead of collapsing to `""`; `StreamAccumulator.IsUnassembled` is the single "never execute this call" predicate (the merge-detection rule stays internal — different purpose, documented); the approver runs in its own try so its failures say "approval", not "tool"; `ToolContext.Events` becomes a filtered sink accepting only extension events (others throw, surfacing as tool errors); `ToolOutput.Truncate` calls guard null output to empty; the digest doc states its scope (messages, model id, tool names — descriptions/schemas deliberately outside); `ReplayChatClient.GetService` returns itself per convention; scripted stream fixtures use model `"synthetic"`; stale T-09-era comments updated.

## Risks / Trade-offs

- [Retrying only event-free attempts is conservative] → correct beats eager: a duplicated stream is worse than a surfaced error; mid-stream failures still end with a repaired history and `RunError`.
- [Conservative classifier may not retry a genuinely transient provider error] → it surfaces as `RunError` with the provider's message; widen with evidence.
- [`length` in the vocabulary widens the closed set] → it is a terminal-reason distinction consumers need; the set stays sealed.

## Migration Plan

Not applicable — pre-release.

## Open Questions

- None blocking.
