# Reasoning end-to-end

## Why

Lunate has no reasoning support anywhere: `AgentEvent` has no reasoning events, `Lunate.Ai`
neither turns provider thinking on nor maps it, and reasoning content that arrives through
Microsoft.Extensions.AI is dropped at the harness. Models that inline their thinking as
` thinking…</think>` text show it raw in the transcript. The reference study (pi/tau) shows
reasoning surfaced as first-class UI — a live thinking stream with a collapsed commit — and
the model catalog already carries providers (Anthropic, OpenAI-compatible) whose reasoning
features users expect to work.

## What changes

- **Events:** `ReasoningStart`, `ReasoningMessageContent`, `ReasoningEnd` (AG-UI names) join
  the closed event set, emitted by the harness when reasoning content streams through MEAI
  (`TextReasoningContent`).
- **Providers:** Anthropic extended thinking (level → budget), OpenAI-compatible reasoning
  effort, and a streaming ` thinking` splitter for models that inline it — selected per model
  by a `reasoning` declaration in the catalog.
- **Correctness:** signed Anthropic thinking blocks go back unchanged within a model's own
  tool-use loops; reasoning is never sent to a provider other than the one that produced it;
  reasoning content persists in the session (schema 2 unchanged — it rides in the embedded
  `ChatMessage`) and survives compaction for the entries that survive it.
- **TUI:** thinking streams live in the thinking color, then commits to the scrollback as a
  collapsed `✦ thought for 12s`; a setting shows the full text; `/thinking
  [off|low|medium|high]` sets the level and the footer shows it.
- **Surfaces:** `--json`/print carries the reasoning events; ACP maps them to
  `agent_thought_chunk` updates.

## What does not change

- No new packages, no pipeline-order change, no schema bump (analysis in `design.md`), no
  behavior change for models without a `reasoning` declaration (they behave exactly as today).
