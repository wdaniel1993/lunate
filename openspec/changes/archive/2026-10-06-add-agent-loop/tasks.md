## 1. Harness surface

- [x] 1.1 `AgentHarness`, `AgentHarnessOptions` (MaxSteps 50, SystemPrompt, WorkingDirectory, Approver), run id, channel wiring (finally-complete), `RunAsync` returning `IAsyncEnumerable<AgentEvent>`; `PublicAPI.Unshipped.txt`
- [x] 1.2 Tests: a text-only scripted run emits RunStarted → TextMessageStart/Content/End → RunFinished and passes the T-07 sequence validator; a throwing provider still completes the stream (RunError then completion)

## 2. Turn loop

- [x] 2.1 Request building (optional system prompt, history, `ChatOptions.Tools` from registry declarations); streaming text events with a generated message id; assistant message appended (aggregated)
- [x] 2.2 Tests: text-only run appends the assistant message; system prompt appears when configured; no system message when null

## 3. Tool execution

- [x] 3.1 Complete calls from the pipeline: per call ToolCallStart → ToolCallArgs (complete JSON) → ToolCallEnd → execute (approver consulted) → ToolCallResult → tool message appended (truncated output); decline and error-result paths; `Details` UI-only
- [x] 3.2 Tests: one tool call then final answer; two calls in one step; multi-step; tool exception → error result and the run continues; unknown tool → error result; malformed args → error result; decline → error result; truncation applied to long output; ToolContext carries the working directory and the shared event sink

## 4. Limits

- [x] 4.1 MaxSteps: count model calls; on exceeding emit StepLimitReached then RunFinished(step_limit)
- [x] 4.2 Tests: a scripted provider that always calls a tool hits the limit at the configured MaxSteps

## 5. Spans

- [x] 5.1 `ActivitySource` "Lunate.Agent"; run span `invoke_agent` + tool child spans `execute_tool` with gen_ai attributes (constants in one file; verify exact names against the pinned conventions) + `lunate.tool.is_error`
- [x] 5.2 Tests: in-memory exporter — one run span with nested model and tool spans; tool span attributes; no listener → no spans

## 6. Replay acceptance

- [x] 6.1 Three scripted sessions recorded once and committed as fixtures (text-only; single tool call; multi-step with two tool calls), replayed end-to-end through the real factory pipeline
- [x] 6.2 Tests: byte-stable event-sequence snapshots (ids normalized); one run span with nested model and tool spans per replayed session

## 7. Close

- [x] 7.1 `scripts/verify.sh` green; `openspec validate add-agent-loop --type change --strict`
- [x] 7.2 Self-review pass; fix findings; commit per group (do NOT spawn subagents; the adversarial review is a separate dispatch)
