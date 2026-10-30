# 0012 — Addendum: context admission

- Status: accepted — 2026-10-30 (maintainer sign-off with conditions (a)–(f), recorded below; change `context-hygiene`)
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

## Constraints accepted with the sign-off (2026-10-30)

Sign-off was given **with six conditions**. (a)–(e) are behaviour this boundary must have and the current
implementation does not yet; each is a 0.2.0 card, because the change above had already been applied. (f) is an
acceptance criterion, not a new mechanism.

- **(a) A spill read is never spilled again — #349.** `read` owns pagination: it returns a bounded page plus an
  explicit continuation (next offset/line), and reads of the session's `tool-output/` paths are exempt from the spill
  step. A 50,000-token output must be reachable in full through successive reads, and no `read` result may itself be a
  spill pointer. The exemption keys on the resolved absolute path of a known spill file, not a prefix a model could
  forge.
- **(b) Projection rewrites happen only at batch points — #350.** Stubbing an older read the moment a newer one arrives
  rewrites history behind the model and invalidates the provider's prompt cache from that point on every following
  request. Read stubs and masking therefore apply at a compaction or every N turns (`projection.batchTurns`,
  configurable), never per request. Between batch points an invalidated older read stays as it is; correctness rests on
  decision 4's guard, not on the stub. The PR reports **cache-read ratio before and after** on the eval run.
- **(c) Observation masking at batch points — #351.** Item 4 of the agreed card, missing from the six decisions above
  and not optional: at a batch point a tool result older than N turns (`projection.maskTurns`) becomes a one-line stub
  carrying its spill pointer where one exists, with error results kept longer (`projection.maskErrorTurns`).
- **(d) An own successful write refreshes the live version — #352.** A successful `edit`/`write` by the agent updates
  the live read version for that path, so consecutive edits of one file work without an intervening re-read. An
  *external* change still invalidates and still demands a re-read; the distinction is the origin of the change. A
  refused write changes nothing.
- **(e) The write guard exists twice — #353.** Decision 5 and the earlier 4a ruling require *both* halves: `write`
  refuses content containing a redaction placeholder **where the target file currently holds the real value**, and
  `edit` refuses an `old_string`/`new_string` containing a placeholder. Only the edit half shipped.
- **(f) Acceptance includes the eval — #351.** The same task success with fewer input tokens, both numbers reported,
  and `/context` showing tokens saved separately by spills, masking and read stubs. Fewer tokens with worse success is
  a rejection.

The public surface this addendum adds (`ToolResult.FullOutput`, `AgentHarnessOptions.SpillThresholdTokens`,
`AgentHarnessOptions.ToolOutputMask`, the settings key) is accepted as written and lands before #237's stable publish.

## Consequences

- Public API grows by `ToolResult.FullOutput`, `AgentHarnessOptions.SpillThresholdTokens` and `AgentHarnessOptions.ToolOutputMask`; the read ledger stays internal between `Lunate.Agent` and the `lunate` executable, so it adds no public surface. A settings key is added; `PublicAPI.Unshipped.txt` and the guide change.
- A model that needs a secret's literal value can no longer read it from a tool result; the marker names what was redacted and the user supplies the value.
- Resume keeps correctness (the seeded ledger requires a re-read) but loses the projection until that re-read; the stale copy is stubbed then.
- Context estimates fall, so compaction fires later. That is the product effect, and the tests measure it through the provider's usage rather than a hand-written count.
- Pattern-based masking and spill-file sweeping are explicit follow-ups, not hidden promises of this decision.
