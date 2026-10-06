## 1. Harness lifecycle

- [x] 1.1 Sequential guard (`Interlocked`, actionable `InvalidOperationException`), linked CTS, read with `CancellationToken.None`, finally cancels + awaits the loop; class doc updated
- [x] 1.2 `AgentHarnessOptions` validation in the harness constructor (`MaxSteps >= 1`, `MaxRetries >= 0`, `RetryBaseDelay >= 0`) + new `MaxRetries` / `RetryBaseDelay` options

## 2. Cancellation and repair

- [x] 2.1 OCE discrimination everywhere (`when (ct.IsCancellationRequested)`; tool path rethrows only real user cancellation, else error result); non-user OCE is a normal failure
- [x] 2.2 History repair (pending-call tracking; synthetic cancelled/aborted results on cancel and on error paths) + `RunFinished(cancelled)` when `RunStarted` was emitted
- [x] 2.3 `StreamModelAsync` closes an open text message in `finally` on any failure

## 3. Retries

- [x] 3.1 `ProviderErrors.IsRetryable` (internal; timeouts, `HttpRequestException`, `ClientResultException` 408/429/500/502/503/504/529; inner-chain walk)
- [x] 3.2 Per-attempt retry loop: exponential backoff under the token, `Retrying(attempt, reason)` per retry, retry only when the attempt emitted no events; exhausted or non-retryable → `RunError`

## 4. Observability

- [x] 4.1 Finish-reason mapping: `StopReasons.Length` + last-turn mapping in `RunFinished`; `UsageUpdated` emitted from `UsageContent`

## 5. Validator

- [x] 5.1 New violation kinds: unclosed text message at terminal, tool call not completed, foreign run id; tests for each

## 6. Small items

- [x] 6.1 Null call ids dedupe by instance reference (no silent merge)
- [x] 6.2 `StreamAccumulator.IsUnassembled` (single predicate; harness uses it and the public fragment key; merge rule documented as internal)
- [x] 6.3 Approver failures get an approval-specific error message; tool event sink accepts only extension events
- [x] 6.4 Null tool output guarded to empty before truncation; `ReplayChatClient.GetService` returns itself; digest scope documented in `FixtureFormat`; scripted stream fixtures marked `"synthetic"`; stale T-09 comments updated
- [x] 6.5 `PublicAPI.Unshipped.txt` for `StopReasons.Length` + `StreamAccumulator.IsUnassembled`

## 7. Tests and acceptance

- [x] 7.1 One replay/scripted test per path: cancel mid-tool, cancel mid-stream (open message closed), abandonment stops the run, concurrent run rejected, OCE-without-token (provider → `RunError`; tool → error result), retry success after transient failure, retries exhausted, non-retryable no-retry, `length` finish, `UsageUpdated`, repair makes the next run valid (assert the repaired history reaches the provider)
- [x] 7.2 Existing snapshots updated where the new terminal/repair events change them; validator tests extended; de-AT pass

## 8. Specs

- [x] 8.1 Deltas in this change: `agent-events` (vocabulary + producers + sequence rules), `agent-loop` (surface, turn semantics, tool safety, new cancellation requirement), `agent-tools` (error text + sink), `ai-layer` (digest scope)

## 9. Close

- [x] 9.1 `scripts/verify.sh` green; `openspec validate add-loop-resilience --type change --strict`; self-review; commit per group
