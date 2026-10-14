## MODIFIED Requirements

### Requirement: Subagent runs

`AgentHarness` SHALL stamp a per-run subagent seam into every `ToolContext` it builds: `ToolContext.Subagents`, an `ISubagentRunner` whose `RunAsync(SubagentRequest request, CancellationToken ct)` starts a child run on the request's prompt and returns a `SubagentResult` (child session id when the parent has a session, child run id, output text, stop reason, error flag, tokens used); `Depth` SHALL report the run's spawn depth (0 for a top-level run). The request MAY carry `MaxSteps` and `AllowedTools`, a tool allow-list naming a subset of the run's own offered tools — the child's registry SHALL be exactly that subset (parent order; an unknown name refuses the spawn with a teaching error naming it; null means the parent's full set) — and `WorkspaceRoot`, the child run's worktree root: it SHALL be canonicalized, null SHALL inherit the parent's workspace, naming the parent's own root SHALL behave as the inherit case, and a missing path or non-directory SHALL refuse the spawn with a teaching error naming the path. Per-spawn values SHALL only tighten the resolved limits (values above the ceiling are clamped, never loosened). The child run SHALL execute on a child `AgentHarness` composed from the parent's live facts — same model client, hooks, mutation queue, file-change sink and approver — with the working directory and the offered tool set following the request's workspace: without a workspace root (or with the parent's own) the child inherits the parent's working directory and the parent's offered tools as allowed; with another root the child runs in that root and its offered tools SHALL be built for it by the composer's workspace-tool factory — absent that factory the spawn SHALL refuse with a teaching error, never run with the parent's tools in the wrong directory — and an allow-list SHALL name a subset of that built set by the same subset rules. The child runs with the parent's system prompt, the request's prompt as user input and its own run id. The child's events SHALL be forwarded onto the parent's event stream with `ParentRunId` set to the parent run id, their own run id and session id intact, and call ids not rewritten; consumers of a stream SHALL scope events by run id, and hook payloads fired during a child run SHALL carry the child's run id and the parent run id. When the parent has a session, a child session file SHALL be created beside the parent's and `AppendChildSession(childSessionId, runId)` SHALL be appended before the child run starts; without a parent session the child still runs and the child session id is null. The child run's cancellation token SHALL be linked to the caller's token — cancelling the call or the parent run cancels the child (it stops `cancelled`) — and `RunAsync` SHALL join the child before returning. The child's tool approvals SHALL go through the parent's approver, which SHALL identify the asking run (run id, parent run id, depth), never bypass it, and a session-wide "always" grant SHALL apply across the whole run tree. Core SHALL enforce, as **teaching error results** naming the limit (and its setting key where the limit is a setting), never exceptions: **depth** — fixed at 1 in this version (a child run's runner refuses every spawn, `depth_limit`; configurable depth arrives later behind a hard ceiling); **concurrency** — `subagents.concurrency` (default 4), tree-wide across every live child run of one top-level run, refused immediately while the limit is held (`concurrency_limit`); **token budget** — `subagents.tokenBudget` (default 100 000 cumulative input+output tokens per child run) and `subagents.treeTokenBudget` (default 500 000 per top-level run tree), checked before each model call (the tighter remaining budget applies; the child stops with `token_limit` and returns its partial result; a spawn with no tree budget left is refused). Limits SHALL resolve from settings (the `subagents` section; `unlimited` only as the explicit literal value) and lower-trust sources — per-spawn requests, and project-scoped overrides when a project settings layer exists — SHALL only tighten them. `SubagentResult.StopReason` SHALL use the run vocabulary (`stop`, `cancelled`, `step_limit`, `length`, `token_limit`) plus the runner refusals (`depth_limit`, `concurrency_limit`, `token_limit`, `workspace_missing`, `workspace_tools_unavailable`) and `error` for a child run that ended in a run error; `IsError` SHALL be true for everything except `stop` and `length`. The child's usage SHALL roll up to the parent run's accounting, and the child session file SHALL keep the child's own usage lines.

#### Scenario: A child run is linked and visible on the parent stream

- **GIVEN** a top-level run with a session and a tool call that runs a subagent
- **WHEN** the child run executes and finishes
- **THEN** the child's events appear on the parent's stream with `ParentRunId` set to the parent run id and their own run id and session id, hook payloads carry both run ids, and the parent session contains a child-session link whose ids equal the child's run id and session id

#### Scenario: The allow-list can only narrow

- **GIVEN** a spawn request naming a subset of the run's tools
- **WHEN** the child runs
- **THEN** only the named tools are offered to the child's model, an unknown name refuses the spawn with a teaching error, and a null list offers the parent's full set

#### Scenario: Approvals route through the parent approver with the asking run named

- **GIVEN** a parent run whose approver records the asking run and denies the child's tool call
- **WHEN** the child requests that tool
- **THEN** the decision comes from the parent's approver (the recording approver saw the request with the child's run id, parent run id and depth) and the child receives the refusal as an error result

#### Scenario: An "always" grant covers the run tree

- **GIVEN** a session-wide "always" granted while a child run asked for a tool
- **WHEN** the parent or a sibling run uses that tool afterwards
- **THEN** no prompt is raised again for it anywhere in the tree

#### Scenario: Cancellation propagates and the runner joins

- **GIVEN** a child run in progress
- **WHEN** the caller's token is cancelled
- **THEN** the child stops with `cancelled`, no further child events are forwarded, and `RunAsync` returns without hanging

#### Scenario: Depth, concurrency and token limits are enforced as results

- **GIVEN** a child run at depth 1, a tree holding `subagents.concurrency` children, and a child whose usage reaches `subagents.tokenBudget` (or the tree's remaining budget)
- **WHEN** further subagent calls execute or the over-budget child reaches its next model call
- **THEN** each spawn refusal is an error result naming its limit and setting key (`depth_limit` / `concurrency_limit` / `token_limit`), the over-budget child stops with `token_limit` returning its partial result, and the session never crashes

#### Scenario: Call ids are scoped by run id, not rewritten

- **GIVEN** a parent and a child whose tool calls both use the call id `call_1`
- **WHEN** their events interleave on the parent stream
- **THEN** every event carries its run id, forwarded child events carry `ParentRunId`, and consumers that key by `(RunId, CallId)` never mix the two calls

#### Scenario: A workspace child runs in its own root

- **GIVEN** a spawn request naming a workspace root other than the parent's and a composer-provided workspace-tool factory
- **WHEN** the child runs and edits a file
- **THEN** the child's working directory and offered tools are built for that root (its write lands there, not in the parent's), and the child session header records that root's repository identity and worktree path

#### Scenario: A workspace request without a tool factory refuses

- **GIVEN** a spawn request naming another workspace root and no composer-provided workspace-tool factory
- **WHEN** the spawn executes
- **THEN** it returns a `workspace_tools_unavailable` teaching error and no child run starts; a missing or non-directory root returns a `workspace_missing` teaching error naming the path
