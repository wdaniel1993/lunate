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

No session schema change in v1. A steering message is a normal `SessionMessageEntry`; its position (after tool results, before the next assistant message) identifies it. The deterministic replay test records the scripted steering run fresh and compares it byte-for-byte against the committed golden `tests/fixtures/sessions/golden-steering.jsonl` (ADR-0015's byte-truth convention; header line excluded as elsewhere). **If implementation proves an origin field is genuinely needed, stop and propose an ADR.**

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

Recorded during apply:

1. **`Lunate.Coding` references `Lunate.Extensibility`** (`src/Lunate.Coding/Lunate.Coding.csproj`): the pinned `InputPipeline` runs `HookRunner.RunInputReceivedAsync`, so the app assembly gains the edge; the layering map (`tests/Lunate.Coding.Tests/LayeringChecker.cs`) records it.
2. **`ApprovalRequested` is not emitted by the harness.** The approval adapter maps the `IToolApprover` invocation (`InteractiveSession.cs`, `SessionApprover`) onto `ApprovalPromptModel(toolName, raw args)`; policy-allowed risks (ask: read-only; auto-edit: write) run without a prompt via `NonInteractiveApprover.IsAllowed`. When event emission lands (T-27/ACP) the adapter can consume the event instead; no loop behavior changed here.
3. **Test seams**: `InteractiveSession.PendingApproval`, `IsRunning`, `QueuedSteeringCount`, and `InteractiveSessionOptions.ConfigureHarness` are internal observation/adjustment points used by the deterministic tests (documented, no production behavior).
4. **Footer reconciliation (the T-21 handoff)**: the live area now formats through `StatusFooterRenderer.PlainText` (compact k/M, one format wins) and `LiveArea.SetFooter` takes a `StatusFooterModel`; the committed Tui frame golden `scripted-session.txt` was regenerated deliberately (the footer line changed from `1540 tok · 12.5% ctx` to `1.5k/12.3k (13%)`).
5. **`length` is treated as non-normal**: no auto-run, leftover to the input line (the spec pins only error and step limit; the conservative reading).
6. **Nested tool results do not commit scrollback blocks** (only top-level `ToolCallResult`s); nested results still drive the live tool line. Committed blocks would duplicate nested output.
7. **CLI entry deferred**: `lunate` with no arguments stays a no-op (`CliTests` pins that); wiring the interactive entry belongs to T-22 part 2 or a follow-up.
8. **`SteeringQueue.Count`** was added beyond the pinned `Enqueue`/`TryDequeue`: the forward test synchronization needs an honest queued-count signal and the frontend can show "N queued".
9. **`AnthropicTurnMerge` is internal and sits above the recorder**, so recorded fixtures capture the wire (merged) request; it merges *all* consecutive wire-user messages — the Anthropic adapter maps every non-assistant message to `user`, so two tool results of one batch would also be consecutive user turns. History/session are untouched (new message instances; originals are never mutated).
10. **Usage accumulation**: `UsageUpdated` counts are summed across events (Anthropic reports input at stream start, output at the end); a provider that sends cumulative totals would double-count — revisit if it bites.
11. **`CompactionApplied` maps to a notice but is not exercised end-to-end** (it needs a compaction-triggering history); `Retrying` and `StepLimitReached` prove the notice channel in the session tests.
12. **Steering replay golden (review fix)**: `tests/fixtures/sessions/golden-steering.jsonl` is committed; the test records fresh and compares byte-for-byte against it (header excluded); regenerate with `LUNATE_UPDATE_STEERING_GOLDEN=1`.
13. **`AnthropicTurnMerge` joins consecutive user texts without a separator** ("one"+"two" -> "onetwo"): unreachable through the frontend (steering is only enqueued while a turn is active, so it always follows tool results); kept minimal deliberately.
14. **`SteeringInjected` is not echoed to scrollback yet** (no `HandleEvent` case); the steering text returns to the input line on `Esc`. An echo block is a part 2 UX follow-up.
15. **CI fix (test infrastructure)**: Spectre's CI detection (`GITHUB_ACTIONS`) force-enables ANSI even past `AnsiSupport.No` in `AnsiConsole.Create`, making the scrollback goldens environment-dependent; the test scrollback now uses `Spectre.Console.Testing.TestConsole` (already used by the Tui goldens, renders plain deterministically; `Coding.Tests` references the same pinned `Spectre.Console.Testing 0.57.2`). The wait helper now yields the thread (`Task.Delay`) under a wall-time deadline instead of a two-million-yield spin, which starved the thread pool on the slow Windows runners. The history-navigation test gates the second model call so the streamed tail is provably painted before the run may end (the run's end commits and clears the tail; ungated, the paint tick raced the commit and lost deterministically on Windows).

## T-22 part 2 / T-53 seams

- Slash commands, model/session pickers and `/command` Tab completion: `RoutedKey.ModelPicker` is handled as a no-op in the session; `LiveArea`/`InputLine` remain unchanged for that work.
- `@path` completion (T-53): the input line still routes `Tab` to `Edit` (no-op), unchanged.
- Steering leftovers are reclaimed through `SteeringQueue.TryDequeue` (Esc / run end); ACP (T-27) can consume `SteeringInjected` and enqueue into the same seam.
