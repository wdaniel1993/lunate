# Design: Interactive session — wiring, steering, Esc (T-22, part 1)

## Structure

- `Lunate.Agent`: `SteeringQueue.cs` (new, public), `AgentHarnessOptions.Steering` (init property, default null = no steering), loop drain in `AgentHarness`, `SteeringInjected` event in `AgentEvent.cs`.
- `Lunate.Ai`: steering merge for the anthropic path — a small delegating `IChatClient` (`AnthropicTurnMerge` or equivalent) applied in `ChatClientFactory` when the provider is anthropic; wire-time only.
- `Lunate.Coding`: `InputPipeline.cs` (hook chain via `HookRunner.RunInputReceivedAsync`; null runner = pass-through), `InputHistory.cs` (`~/.lunate/history`, JSONL), `InteractiveSession.cs` + renderers glue (the app class; lives here because `Lunate.Coding` references Agent/Ai/Tui — the Tui project never references upward).
- `adr/0013-agent-loop.md`: dated amendment section + status line note.
- Tests: `Lunate.Agent.Tests` (steering order), `Lunate.Ai.Tests` (merge + fixtures), `Lunate.Coding.Tests` (pipeline, history, session units), E2E snapshot in `Lunate.Coding.Tests` with `FakeConsoleIO` + `ScriptedChatClient`/recorded streams.

## Steering semantics (pinned)

- Drain point: before each model request build, after every tool result of the batch is appended. Falsifier test: a batch of N calls produces call/result pairs adjacently; steering lands after the batch's last result and before the next assistant message.
- Each injected message: append user `ChatMessage` to history; append `SessionMessageEntry` when a session is attached (parentId = last entry id — this position is the origin; no schema change); emit `SteeringInjected(runId, entryId)` (entryId null without a session); `InputReceived` already ran at the input layer, the harness does not re-run it.
- Leftover after the run ends stays in the queue; `SteeringQueue` exposes `TryDequeue` for the frontend. v1 drains only the top-level run (nested calls never see the queue).
- The harness never auto-runs anything; auto-starting the next run is the frontend's rule.

## Anthropic wire merge (pinned)

Anthropic requires alternating turns; a steering user message appended right after tool results would produce two consecutive user turns. At wire time, for the anthropic path only, consecutive user messages are merged: the steering text is appended into the tool-results user turn. OpenAI-style providers keep the separate user message. History and session stay as appended (the merge is transport, not truth). Tests: unit tests on the merge with realistic message shapes + the recorded fixtures `anthropic-basic.jsonl` and `opencode-go-basic.jsonl` as provider-shaped inputs.

## Session origin stance

No session schema change in v1. A steering message is a normal `SessionMessageEntry`; its position (after tool results, before the next assistant message) identifies it. The deterministic replay test re-injects steering at the step identified by that position and compares the resulting session bytes with the recorded one, using the existing replay conventions (fixed ids/clock as those tests already do). **If implementation proves an origin field is genuinely needed, stop and propose an ADR.**

## Interaction rules (pinned from the maintainer)

- `Esc`: cancels the run; queued steering stays unsent and is returned to the input line (joined with `\n` when multiple).
- Run finished normally with leftover steering: auto-start the next run with the leftover text.
- Run ended in error or step limit: no auto-run; leftover returns to the input line.
- Approval prompt open: the input line accepts no new text (no steering); prompt keys only (`y`/`n`/`a`; Enter denies); global quit keys (Ctrl+C window) still work.
- "Always" memory: session-scoped set of tool names held by the approver adapter; `IToolApprover` unchanged.
- Streaming: completed paragraphs are rendered as Markdown and committed to scrollback; the unfinished tail stays in the live area. Tool blocks commit on `ToolCallResult`. Footer updates on `UsageUpdated`.

## Input pipeline

`InputPipeline.ProcessAsync(text)` → `RunInputReceivedAsync(InputReceivedPayload(text))`: `PassThrough` → text; `Transform` → transformed text; `Consume` → nothing starts/queues, a dim notice is shown. The pipeline is used by the session for both normal sends and steering enqueues. A null `HookRunner` passes through (tests and builds without extensions).

## Out of scope

Slash commands, pickers, `/command` Tab completion (T-22 part 2); `@path` completion (T-53); ACP parity for steering (T-27 consumes the same event); approval risk display.

## Deviations

(Filled during apply; empty at proposal time.)
