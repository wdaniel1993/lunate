# 0012 — Addendum: context admission

- Status: proposed — drafted with the `context-hygiene` change (#187); awaiting maintainer sign-off
- Date: 2026-10-28
- Relates to: ADR-0012 (tool contract), ADR-0003 (own loop), ADR-0013 (agent loop), ADR-0015 (session format), ADR-0016 (workspace boundary)

## Context

ADR-0012 decision 3 bounded a tool result at 30,000 characters with a head/tail marker. That was right for a loop whose only cost was memory; it is wrong for a model context, where 30,000 characters is about 7,500 tokens and a long session pays it again on every request. #196 then placed exact-value credential redaction at the persistence boundary and said so explicitly: the session and spill files are redacted, the in-memory result the model sees is not. That leaves the request as the one boundary nothing guards.

Four behaviours now meet on the same path: a large result must leave the window (spills), a tool must be able to send its answer without the transcript of how it produced it (concise results), a read that an edit invalidated must not be trusted or paid for again (stale reads), and a secret a tool echoes must not enter the request (masking). They are one decision because they all answer the same question: what does the loop admit into the model's context, and in what order.

## Decision

1. **The loop is the admission boundary.** Every tool result passes mask → project (`FullOutput`) → spill → bound → append. The `ToolCallResult` event, history message and session entry carry the same admitted text; the spill file carries the masked full text. Masking runs first so nothing downstream can persist, emit or truncate a raw value.
2. **Spill by estimated tokens, not characters.** `output.spillThresholdTokens` (default 2,000, UTF-8 bytes / 4) decides when a result leaves the window; `output.toolResultLimit` stays as the absolute fallback cap. The in-band text is a bounded preview (10 head and 10 tail lines, at most 2,000 characters) plus a pointer naming the path, the size and the next action. The spill file is the existing per-session `tool-output/<run>-<seq>.txt`; it becomes readable through `read` as a per-run allowance, because a pointer the model cannot follow is not a spill.
3. **Tools own concision.** `ToolResult.FullOutput` is the transcript behind a concise `Output`. The loop spills the transcript and never derives which lines matter from content. A tool that does not set it is unchanged.
4. **Reads are versioned.** The newest read of a path is live; older read results are superseded and stubbed in the in-memory history that builds the next request. `edit`/`write` refuse when the live version was invalidated or its content digest differs, and say why. The rule is "do not trust an invalidated read", not "you must have read it": a path the ledger never saw behaves as before. The session keeps the original entry; the projection is request-time.
5. **Masking before context is exact-value only.** The host supplies one exact-value redactor for session and `--no-session` runs. Pattern detection stays out: a filter that guesses is a filter readers learn to trust, and this repository has already decided not to build one.
6. **No session format change.** The session stores the admitted text and the provider's usage for the request as sent. Persisting read versions, which would keep the stale projection across a resume, is a later decision that needs a schema bump.

## Consequences

- Public API grows by `ToolResult.FullOutput`, `ToolContext.Reads`/`IReadLedger`, `AgentHarnessOptions.SpillThresholdTokens` and `AgentHarnessOptions.ToolOutputMask`; a settings key is added; `PublicAPI.Unshipped.txt` and the guide change.
- A model that needs a secret's literal value can no longer read it from a tool result; the marker names what was redacted and the user supplies the value.
- Resume keeps correctness (the seeded ledger requires a re-read) but loses the projection until that re-read; the stale copy is stubbed then.
- Context estimates fall, so compaction fires later. That is the product effect, and the tests measure it through the provider's usage rather than a hand-written count.
- Pattern-based masking and spill-file sweeping are explicit follow-ups, not hidden promises of this decision.
