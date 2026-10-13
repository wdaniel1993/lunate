## ADDED Requirements

### Requirement: Subagent runs

`AgentHarness` SHALL stamp a per-run subagent seam into every `ToolContext` it builds: `ToolContext.Subagents`, an `ISubagentRunner` whose `RunAsync(SubagentRequest request, CancellationToken ct)` starts a child run on the request's prompt and returns a `SubagentResult` (child session id when the parent has a session, child run id, output text, stop reason, error flag); `Depth` SHALL report the run's spawn depth (0 for a top-level run). The child run SHALL execute on a child `AgentHarness` composed from the parent's live facts — same model client, tool registry, working directory, hooks, mutation queue, file-change sink and approver — with the parent's system prompt, the request's prompt as user input and `MaxSteps` from the request when given, and its own run id. The child's events SHALL be forwarded onto the parent's event stream with `ParentRunId` set to the parent run id, their own run id and session id intact, and call ids not rewritten; consumers of a stream SHALL scope events by run id. When the parent has a session, a child session file SHALL be created beside the parent's and `AppendChildSession(childSessionId, runId)` SHALL be appended before the child run starts; without a parent session the child still runs and the child session id is null. The child run's cancellation token SHALL be linked to the caller's token — cancelling the call or the parent run cancels the child (it stops `cancelled`) — and `RunAsync` SHALL join the child before returning. The child's tool approvals SHALL go through the parent's approver, never bypass it. Core SHALL enforce depth (`AgentHarnessOptions.MaxSubagentDepth`, default 2), concurrency (`MaxConcurrentSubagents`, default 4, a call beyond it refused immediately while the limit is held) and token budget (`SubagentTokenBudget`, default 100 000 input+output tokens per child run; exceeding it cancels the child) as **teaching error results** naming the limit (`depth_limit`, `concurrency_limit`, `token_limit`), never exceptions. `SubagentResult.StopReason` SHALL use the run vocabulary (`stop`, `cancelled`, `step_limit`, `length`) plus the three refusal reasons; `IsError` SHALL be true for everything except `stop` and `length`.

#### Scenario: A child run is linked and visible on the parent stream

- **GIVEN** a top-level run with a session and a tool call that runs a subagent
- **WHEN** the child run executes and finishes
- **THEN** the child's events appear on the parent's stream with `ParentRunId` set to the parent run id and their own run id and session id, and the parent session contains a child-session link whose ids equal the child's run id and session id

#### Scenario: Approvals route through the parent approver

- **GIVEN** a parent run whose approver records and denies the child's tool call
- **WHEN** the child requests that tool
- **THEN** the decision comes from the parent's approver (the recording approver saw the request) and the child receives the refusal as an error result

#### Scenario: Cancellation propagates and the runner joins

- **GIVEN** a child run in progress
- **WHEN** the caller's token is cancelled
- **THEN** the child stops with `cancelled`, no further child events are forwarded, and `RunAsync` returns without hanging

#### Scenario: Depth, concurrency and token limits are enforced as results

- **GIVEN** a run at `MaxSubagentDepth`, a run holding `MaxConcurrentSubagents` children, and a child whose usage exceeds `SubagentTokenBudget`
- **WHEN** further subagent calls execute
- **THEN** each refusal is an error result naming its limit (`depth_limit`, `concurrency_limit`, `token_limit`), the over-budget child is cancelled, and the session never crashes

#### Scenario: Call ids are scoped by run id, not rewritten

- **GIVEN** a parent and a child whose tool calls both use the call id `call_1`
- **WHEN** their events interleave on the parent stream
- **THEN** every event carries its run id, forwarded child events carry `ParentRunId`, and consumers that key by `(RunId, CallId)` never mix the two calls
