# Reasoning end-to-end — design

Reference: TUI epic Step 0 sign-off (2026-10-18, "Sounds good! Let's implement and check."),
item 1. Visual contract: `visual-spec.md` §3.3 (thinking), §4 (working indicator, footer).

## D1. Event shapes (AG-UI names)

```csharp
public sealed record ReasoningStart(string RunId, string MessageId) : AgentEvent(RunId);
public sealed record ReasoningMessageContent(string RunId, string MessageId, string Text) : AgentEvent(RunId);
public sealed record ReasoningEnd(string RunId, string MessageId) : AgentEvent(RunId);
```

- One reasoning "message" per provider reasoning span; `MessageId` from the same
  `RunIds.NextMessage()` sequence as text messages (run-unique ids; consumers scope by run id).
- Bracketing mirrors text: content events only between start and end; the end is emitted in a
  `finally` when the stream ends mid-reasoning (same repair semantics as `TextMessageEnd`).
- Reasoning and text do not interleave within one bracket; the loop emits sequential spans
  (reasoning typically precedes text; a provider that alternates produces alternating
  brackets, never nested).
- Sequence validator extends: an open reasoning message at the terminal event is a violation
  (same as open text).

## D2. Mapping (harness `StreamModelAsync`)

The content loop (currently `TextContent` / `UsageContent`) gains `TextReasoningContent`:

- Non-empty `Text` fragments → `ReasoningStart` (once) + `ReasoningMessageContent` per
  fragment; `ReasoningEnd` when the span closes (first non-reasoning content after it, or
  stream end).
- `attempt.Emitted = true` on the first reasoning fragment (a reasoning-only exchange still
  counts as an emitted attempt — same as text).
- The assembled assistant message comes from `updates.ToChatResponse()` — MEAI aggregates
  `TextReasoningContent` (including `ProtectedData`, the Anthropic signature) into the
  message, so persistence rides along with no assembly change. `ReplaceMessageText` already
  preserves non-`TextContent` contents.
- The inline splitter (D4, style `inline` only) runs over `TextContent` fragments before the
  text events; it emits reasoning spans through the same event path.

## D3. Provider selection — `reasoning` in the catalog

`ModelInfo` gains an optional `Reasoning` style: `"effort"` | `"budget"` | `"inline"`
(absent = no reasoning support; the model behaves exactly as today). Declared in
`models.json` like every catalog field; user files can override.

- `effort` — OpenAI-compatible reasoning effort. Level maps: low→`Low`, medium→`Medium`,
  high→`High`, off→none. Built through `ChatOptions.Reasoning` where the adapter maps it,
  else the provider's raw field (decided per adapter in the apply; the level→value table is
  the contract).
- `budget` — Anthropic extended thinking. Level maps to `budget_tokens`:
  low→2,048, medium→8,192, high→32,768 (off→none). The adapter request needs `max_tokens`
  set when thinking is on (Anthropic requirement); the budget is clamped below it.
- `inline` — the model emits ` thinking…</think>` in its text stream; no provider parameter
  is sent. The splitter (D4) handles display and events.

A model whose style is absent sends no reasoning parameters regardless of `/thinking`.

## D4. The inline ` thinking` splitter

A small streaming state machine over text fragments (style `inline` only), tolerant of tags
split across fragments (buffers at most `len("")` tail characters until a decision):

- Text inside ` thinking…</think>` → reasoning events; outside → text events.
- The tags themselves are never emitted (neither as text nor reasoning).
- Unclosed ` thinking` at stream end: everything from the tag on is reasoning; the end is
  bracketed in the `finally`.
- Case: exact lowercase ` thinking` / `</think>` only (the convention across
  Qwen/DeepSeek/MiniMax-style models); configurable variants are out of scope.

## D5. Persistence and the schema question (no bump)

Reasoning content persists inside the embedded `ChatMessage` (the schema-2 contract already
says "exactly as `AIJsonUtilities` serializes it"; `TextReasoningContent` including
`ProtectedData` is an MEAI type with symmetric serialization). Consequences, accepted and
tested:

- **Forward compatibility:** an older build loading a schema-2 session that contains
  reasoning content fails on the unrecognized `$type` (existing designed behavior for
  unknown content discriminators). Accepted: sessions are per-user artifacts, and the
  alternative (a schema bump) would not help older builds either.
- **Byte-identity:** a golden round-trip test with reasoning content (including
  `ProtectedData`) proves the format guard holds.
- **Compaction:** reasoning in entries the summary replaces is dropped with them (the rebuilt
  history contains only surviving entries). Post-compaction assistant entries keep their
  reasoning. `MessageText`/`ToContextMessage` ignore reasoning (summaries and hooks never see
  it as text).

## D6. Provider-scoped requests (the safety rule)

"Never send reasoning to a provider other than the one that produced it":

- Each assistant entry records its model id (already the case). When building a request, the
  harness strips `TextReasoningContent` from history messages whose recorded model id differs
  from the current call's model id.
- Same-model loops keep the blocks (Anthropic requires its signed blocks back unchanged
  within a tool-use loop; the same model id covers it).
- Stripping happens on the request copy only — never in the session file.

## D7. Thinking levels and settings

- Level vocabulary: `off` | `low` | `medium` | `high` (default `off`). `/thinking <level>`
  sets it; bare `/thinking` prints the current level and the mapping for the active model.
- Persisted in `~/.lunate/settings.json` as `"thinking": "<level>"` (sticky across sessions;
  an invalid value fails validation like `approval`).
- `"thinkingFull": true|false` (default false) — the display setting for the full reasoning
  text (D8). Set in settings.json only (no slash command in this change).
- The footer shows `think:<level>` when the level is not `off`.

## D8. TUI contract (per the visual spec)

- Live: the reasoning text streams in the live area above the input box in the `thinking`
  color, prefixed `⠹ thinking… <elapsed>s`; capped at 12 visible lines (the rest scrolls in
  place, newest last). The working indicator integrates (phase `thinking`).
- Commit: when `ReasoningEnd` arrives, the live block commits to the scrollback as one
  collapsed line `✦ thought for <n>s` (thinking color; `n` = whole seconds, start→end). With
  `thinkingFull`, the full text commits instead (wrapped, thinking color, indented 2).
- NO_COLOR/16-color: the glyph + word carry the meaning (spec §1 rule).
- Rendering consumes events only — the same renderers work for replayed fixtures.

## D9. Surfaces

- **print `--json`:** the generic event serializer already covers "every event"; the new
  types serialize with their stamped fields. A scenario pins it.
- **ACP:** `ReasoningStart/Content/End` map to `agent_thought_chunk` session updates
  (AG-UI `THINKING_*` semantics); nothing else changes in the ACP surface.

## D10. Out of scope

- Reasoning for subagents beyond event forwarding (they ride the same path).
- Provider-specific reasoning summaries (OpenAI `summary`), redacted-thinking handling.
- Cost meters; reasoning token accounting (usage already flows; no new counters).

## Testing contract (per the epic's per-card rules)

- Unit: event bracketing + validator extension; splitter (fragment splits at every tag
  boundary); level→param mapping per style; request filter (same-model keeps, cross-model
  strips).
- Persistence: golden round-trip with reasoning + `ProtectedData`; compaction path.
- Composition: a production-composition test through the real host (a scripted fixture with
  reasoning updates → live thinking frame → collapsed commit line).
- Snapshots: thinking states at 120/80/50 × dark/light/none.
- VHS: one tape driven by the reasoning fixture (README media + regression from one file).
- Startup budget unchanged; no new packages; no pipeline-order change.
