# Design: add-compaction

## Context

The guide pins the approach (lines 278-312): our strategy implements `IChatReducer` (not `ReducingChatClient`); the loop calls it before each model request, so it can emit `CompactionApplied` and write the compaction entry. MEAI 10.10.x ships `SummarizingChatReducer`/`MessageCountingChatReducer` (both `[Experimental]`) — evaluate as a base before writing custom reduction logic; a base reducer is only acceptable within our rules (never split tool pairs; keep system prompt/AGENTS.md/last turns verbatim; record a compaction entry), otherwise wrap, never weaken. If MEAI's experimental surface is unusable without suppressions we don't want, implement the reduction ourselves behind the same shape. No new packages; no ADR.

## Trigger and estimate

- Before each model request: estimate = `utf8Length/4` of the composed message list (chars/4, documented, invariant).
- Correction: if the last `UsageReported` for this session reported input tokens and no compaction happened since, estimate = `lastInputTokens + deltaChars/4` (delta = characters added since that request). Documented rule; also correct after a summarization call.
- Window: from the model catalog (`ModelCatalog`); when unknown, fall back to a documented default (e.g. 128k) — never crash, log once.
- Forced: `AgentHarness.CompactNowAsync()` (or an equivalent options/flag API) compacts regardless of the threshold — the surface the future `/compact` binds to. Tests use it.

## Reduction

- Keep-verbatim tail: system prompt (all system messages, incl. AGENTS.md content), the last N turns (default 4, configurable), and their complete tool call/result pairs — the boundary expands forward/backward so a pair is never split.
- Summarize everything older in ONE call to the same `IChatClient` with a fixed summarization prompt: goal, decisions, files touched, open problems. The response becomes the summary.
- Usage of the summarization call: counted toward the run's usage totals and logged; not emitted as a separate `UsageUpdated` (it is not a user-visible turn) — documented.
- Failure (provider error, empty summary, cancellation): leave the request unchanged, report, continue — fail-open for availability; the threshold stays exceeded, so it retries next request (bounded by reporting).

## Outputs

- `Session.AppendCompaction(summary, replaces)` — `replaces` = the ids of the entries the summary replaces (compile from the session's message entries older than the tail). The session file keeps full history; `ToHistory()` was not compaction-aware — this change makes it so (summary + messages after the replaced entries; scenario in the delta).
- `CompactionApplied(runId, replacedEntryIds, estimatedTokensAfter)` event (fields pinned here; the event type already exists in the closed set).
- Next request: system + [compaction summary as the context message] + kept tail — under 60% of the window in the recorded test.

## Hook wiring (`Compacting`)

- The loop's compaction point invokes the harness seam (`IAgentHookPoints.Compacting`) before summarization; the adapter maps to the T-37 `Compacting` DTOs as implemented.
- Semantics per the catalogue: last non-null `Provide(summary)` wins; `UseDefault` → the default summarization; handler failure (throw/timeout) → fall back to the default and report (policy: fall back).
- When hooks are unconfigured, behavior is unchanged (no-op seam).

## Testing strategy

- Recorded long session (temporary `RecordingChatClient` pattern; recorder not committed): compacts exactly once, next request < 60% of window, replay matches byte-identically; `CommittedFixtureTests` covers the fixture.
- Tool-pair safety: boundary cases (pair exactly at the tail edge both directions). Usage-corrected estimate. Unknown window doesn't crash. Forced compaction. Hook: `Provide` overrides; failure falls back (default summary still written). Resume: `ToHistory()` after a compacted session yields summary + tail; request rebuild uses it.
- Both cultures (de-AT); invariant formatting.

## Out of scope

The `/compact` slash command binding (T-22 TUI), TUI rendering of the compaction notice (T-20s), memory-provider re-injection (T-44 — this change only provides the hook + event the sample builds on).
