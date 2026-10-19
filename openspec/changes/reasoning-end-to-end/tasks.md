# Reasoning end-to-end — tasks

Design: `design.md` (D1–D10). Sign-off: TUI epic Step 0 (2026-10-18). Implement groups in
order; commit per group; record any design/code divergence in this file under the item.

## 1. Events + mapping

- [x] 1.1 Add `ReasoningStart`, `ReasoningMessageContent`, `ReasoningEnd` to
  `src/Lunate.Agent/AgentEvent.cs` (sealed records, D1 shapes; same `RunIds.NextMessage()`
  id sequence).
- [x] 1.2 Map `TextReasoningContent` in `AgentHarness.StreamModelAsync`: bracketed events,
  `attempt.Emitted` on first fragment, `ReasoningEnd` in the `finally` when the stream ends
  mid-reasoning (mirror the text bracket exactly).
- [x] 1.3 Extend `EventSequenceValidator`: open reasoning at the terminal event is a
  violation; content outside a bracket is a violation.
- [x] 1.4 Tests: bracketing (start/content/end order), reasoning-then-text sequencing,
  mid-stream provider failure closes the bracket, validator red-checks.
- [x] 1.5 Public API: add the new types to `PublicAPI.Unshipped.txt` (RS0016 lines).

## 2. Provider support

- [x] 2.1 `ModelInfo` gains `Reasoning` (style string: `effort` | `budget` | `inline`,
  absent = none); catalog parse + merge + tests (user override of the field included).
- [x] 2.2 Level→provider-options mapping per style (D3 table), built where `BuildOptions`
  composes `ChatOptions`; `off`/absent send nothing. Tests per style.
  (Apply decision: for `budget` the pinned Anthropic adapter's Extended mode derives its own
  budgets (1,024/8,192/16,384), not the D3 table, so the exact budget rides the provider's raw
  field per D3: `RawRepresentationFactory` builds `ThinkingConfigEnabled` with the table's
  budget, `max_tokens` clamped above it.)
- [x] 2.3 Anthropic: when style `budget` and level on, enable extended thinking with the
  budget and ensure `max_tokens` is present and clamped above the budget. Live contract test
  (`LUNATE_LIVE=1` + key): signed thinking blocks round-trip through a tool loop unchanged.
  (The budget rides `ReasoningParameters`' raw-field request; a deterministic transport test
  pins the wire request (`thinking.type=enabled`, `budget_tokens`, `max_tokens` above it) and
  the reasoning/signature assembly; the live test drives the tool loop.)
- [x] 2.4 The inline ` thinking` splitter (D4): streaming state machine over text fragments,
  tag splits tolerated (buffer ≤ the tag length), unclosed tag closes at stream end; unit
  tests split at every boundary.
- [x] 2.5 Built-in `models.json`: declare styles for the models known to support reasoning
  (keep conservative; absent is always safe).

## 3. Persistence + safety

- [x] 3.1 Golden session round-trip with reasoning content incl. `ProtectedData`
  (byte-identity guard).
- [x] 3.2 Request filter (D6): strip `TextReasoningContent` from history messages whose
  recorded model id differs from the current call's model; same-model keeps. Tests both ways
  + the session file keeps the content.
- [x] 3.3 Compaction path: replaced entries' reasoning is dropped with them; surviving
  assistant entries keep theirs; the rebuilt history is valid (test).
- [x] 3.4 Settings: `thinking` (level, default `off`, validated) and `thinkingFull`
  (default false) in `settings.json`; validation tests mirroring `approval`.

## 4. TUI

- [x] 4.1 Thinking live block: streams in the `thinking` color, `⠹ thinking… <elapsed>s`
  prefix, 12-line cap, participates in the working indicator phase `thinking`.
  (Divergence: no theme resolver exists yet, so the colour is the one `ThinkingColor` helper
  (`#af87d7`; truecolor / 16-colour magenta / none per capability) — the theme card moves it
  behind the resolver. The live block keeps its own 12-line window, so a thinking frame may
  exceed the 8-line `MaxLines` bound the rest of the live area keeps.)
- [x] 4.2 Collapsed commit on `ReasoningEnd`: `✦ thought for <n>s` (whole seconds); with
  `thinkingFull`, commit the full wrapped text (2-space indent).
  (Divergence: the collapsed line commits flush left — D8 spells it without the visual-spec
  snapshot's leading indent; the snapshot pass can settle it. An empty full text commits
  nothing.)
- [x] 4.3 `/thinking [off|low|medium|high]` (bare = print level + mapping); footer segment
  `think:<level>` when not `off`.
  (Divergence: the level is session-scoped — set in memory, no settings.json write-back; the
  bare form is read-only and allowed while a turn runs, the set form refuses like `/model`.)
- [x] 4.4 Tests: unit (renderers) + the production composition test (scripted reasoning
  fixture through the real host: live frame → collapsed commit).
- [x] 4.5 Snapshot tests for the thinking states (live block, collapsed commit, full commit) at
  120/80/50 × truecolor/16-colour/NO_COLOR.
  (No light/dark dimension exists yet: `ThinkingColor` (4.1) is the interim capability seam, so
  the goldens cover its three modes and the light value lands with the theme card. The snapshot
  pass settled 4.2's indent question: the collapsed line stays flush left per D8; the
  visual-spec snapshot's leading indent is deferred to the theme card. It also settled the
  commit colour: Spectre's hex-to-16-colour conversion picked white, so
  `ThinkingCommitRenderer` now takes the capability and selects magenta explicitly — the
  committed form matches the live block's `\x1b[35m`.)

## 5. Surfaces

- [x] 5.1 `--json`: the reasoning events serialize with stamped fields (scenario test).
- [x] 5.2 ACP: reasoning events → `agent_thought_chunk` updates (mapper test).
  (Divergence: D9 names Start/Content/End, but the pinned ACP schema has only the chunk-style
  updates — `agent_message_chunk`, `agent_thought_chunk`, `user_message_chunk` — and no thought
  start/end counterpart, so the mapping is content-only: `ReasoningMessageContent` →
  `agent_thought_chunk`; `ReasoningStart`/`ReasoningEnd` map to nothing rather than a forced
  variant.)

## 6. Media + verification

- [x] 6.1 A scripted reasoning fixture (model id `synthetic`) for replay-driven rendering.
- [x] 6.2 A VHS tape (`docs/media/tapes/features-thinking.tape`) via the replay client;
  extend `scripts/render-media.sh`; GIF committed under `docs/media/features/`.
  (Divergence: a replay checks the digest of the whole request, and the 6.1 fixture carries the
  composition test's request shape — its short system prompt and four tools. The render script
  therefore derives its scratch fixture from the committed one, replacing only the digest with the
  tape request's (`THINKING_DIGEST`, pinned like the demo fixture's environment); the updates stay
  single-sourced.)
- [ ] 6.3 Startup budget unchanged (hyperfine check in the gate).
  (Covered by the change gate; left open here because the gate's startup check is not captured in this file.)

## 7. Docs

- [ ] 7.1 `docs/guide.md` event table row (done in draft) + the loop step note.
- [x] 7.2 Document `reasoning` in the models.json docs (README or the catalog section).
