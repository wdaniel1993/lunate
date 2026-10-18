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
- [ ] 2.3 Anthropic: when style `budget` and level on, enable extended thinking with the
  budget and ensure `max_tokens` is present and clamped above the budget. Live contract test
  (`LUNATE_LIVE=1` + key): signed thinking blocks round-trip through a tool loop unchanged.
- [ ] 2.4 The inline ` thinking` splitter (D4): streaming state machine over text fragments,
  tag splits tolerated (buffer ≤ the tag length), unclosed tag closes at stream end; unit
  tests split at every boundary.
- [ ] 2.5 Built-in `models.json`: declare styles for the models known to support reasoning
  (keep conservative; absent is always safe).

## 3. Persistence + safety

- [ ] 3.1 Golden session round-trip with reasoning content incl. `ProtectedData`
  (byte-identity guard).
- [ ] 3.2 Request filter (D6): strip `TextReasoningContent` from history messages whose
  recorded model id differs from the current call's model; same-model keeps. Tests both ways
  + the session file keeps the content.
- [ ] 3.3 Compaction path: replaced entries' reasoning is dropped with them; surviving
  assistant entries keep theirs; the rebuilt history is valid (test).
- [ ] 3.4 Settings: `thinking` (level, default `off`, validated) and `thinkingFull`
  (default false) in `settings.json`; validation tests mirroring `approval`.

## 4. TUI

- [ ] 4.1 Thinking live block: streams in the `thinking` color, `⠹ thinking… <elapsed>s`
  prefix, 12-line cap, participates in the working indicator phase `thinking`.
- [ ] 4.2 Collapsed commit on `ReasoningEnd`: `✦ thought for <n>s` (whole seconds); with
  `thinkingFull`, commit the full wrapped text (2-space indent).
- [ ] 4.3 `/thinking [off|low|medium|high]` (bare = print level + mapping); footer segment
  `think:<level>` when not `off`.
- [ ] 4.4 Tests: unit (renderers) + the production composition test (scripted reasoning
  fixture through the real host: live frame → collapsed commit).
- [ ] 4.5 Snapshot tests for the thinking states at 120/80/50 × dark/light/NO_COLOR.

## 5. Surfaces

- [ ] 5.1 `--json`: the reasoning events serialize with stamped fields (scenario test).
- [ ] 5.2 ACP: reasoning events → `agent_thought_chunk` updates (mapper test).

## 6. Media + verification

- [ ] 6.1 A scripted reasoning fixture (model id `synthetic`) for replay-driven rendering.
- [ ] 6.2 A VHS tape (`docs/media/tapes/features-thinking.tape`) via the replay client;
  extend `scripts/render-media.sh`; GIF committed under `docs/media/features/`.
- [ ] 6.3 Startup budget unchanged (hyperfine check in the gate).

## 7. Docs

- [ ] 7.1 `docs/guide.md` event table row (done in draft) + the loop step note.
- [ ] 7.2 Document `reasoning` in the models.json docs (README or the catalog section).
