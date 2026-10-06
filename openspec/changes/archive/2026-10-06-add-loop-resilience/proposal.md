## Why

Card T-10 (error paths: cancel, retries) plus the cancellation cluster a full-repo review (2026-10-06) found on the loop: cancelling or abandoning a run leaves tool calls without results in the history (the next run is then rejected by both providers); any `OperationCanceledException` — including HTTP timeouts — is treated as user cancellation and ends the run silently with no terminal event; abandoning the stream does not stop the run and a second concurrent run corrupts the shared history; a provider failure mid-text leaves `TextMessageStart` open; the validator misses unclosed messages, incomplete calls and foreign run ids; retries exist in the guide but not in the loop; model finish reasons and usage are dropped (a `length` cut-off reports `stop`); and several smaller loop items from the same review. This change makes cancellation, retries and abnormal termination correct, observable and testable.

## What Changes

- **Cancellation**: runs are enforced sequential; abandoning the stream cancels the run; user cancellation repairs the history (synthetic cancelled results for calls without results) and ends with `RunFinished(cancelled)`; non-user `OperationCanceledException`s (timeouts) are ordinary failures — retried when transient, `RunError` otherwise.
- **Retries**: transient provider errors retry up to 3 times with exponential backoff, emitting `Retrying` per attempt; only attempts that emitted no events retry (no duplicate text); a conservative `ProviderErrors` classifier decides retryability; delays are configurable for tests.
- **Stream integrity**: a failing stream closes its open text message first; the validator reports unclosed messages, incomplete tool calls and foreign run ids.
- **Observability**: finish reasons map (`length` → `length`, unknown → `stop`); `UsageUpdated` gets its producer.
- **Small loop items**: `MaxSteps` validated; null call ids no longer merge distinct calls; one shared unassembled-call predicate; approver failures get their own message; tools can only emit extension events; the replay digest scope is documented; `ReplayChatClient.GetService` follows the convention; scripted fixtures are marked synthetic; stale comments updated; null tool output cannot crash the loop.
- Out of scope: a provider-level tool error flag (decided: the error reaches the model through instructing output text; revisit with real tools and a live fixture), steering (T-22), compaction (T-23).

## Capabilities

### New Capabilities
- None.

### Modified Capabilities
- `agent-loop`: sequential runs, abandonment, cancellation + history repair, retries, approver and sink rules.
- `agent-events`: `length` stop reason, retry/usage producers, stricter sequence rules.
- `agent-tools`: error results reach the model as output text; tools may only emit extension events.
- `ai-layer`: replay digest scope documented.

## Impact

- `src/Lunate.Agent/`: `AgentHarness.cs`, `AgentHarness.Tools.cs`, `AgentHarnessOptions.cs`, `AgentEventChannel.cs` (doc), `EventSequenceValidator.cs`, `StopReasons.cs`, new `ProviderErrors.cs` + internal tool event sink; `PublicAPI.Unshipped.txt`.
- `src/Lunate.Ai/`: `StreamAccumulator.cs` (`IsUnassembled`), `ReplayChatClient.cs`, `FixtureFormat.cs` (doc), synthetic fixture headers.
- Tests: new cancel/retry/repair/validator/finish/usage cases + updated snapshots; fixtures regenerated where digests change. No new packages; no ADR (the loop structure is ADR-0013; this fills its semantics).
