# agent-loop Specification

## Purpose
The agent loop contract: one run as an event stream (`AgentHarness.RunAsync`), the turn semantics between model calls and tool execution, safety inside the loop (instructing error results, truncation, the approval seam), and the run and tool spans that make a run inspectable.

## Requirements

### Requirement: Harness surface
`Lunate.Agent` SHALL expose the loop as `AgentHarness.RunAsync(userInput, ct)` returning `IAsyncEnumerable<AgentEvent>`. One run SHALL carry exactly one run id, every event of the run SHALL travel the same ordered emission path (tools included, through `ToolContext.Events`), and the stream SHALL complete when the run ends — including when it ends in an error or in cancellation. Runs on one harness SHALL be sequential: a second concurrent run SHALL fail with an actionable error, and abandoning the stream SHALL stop the run.

#### Scenario: Events arrive in order and the sequence is valid
- **GIVEN** a scripted provider with a text-only answer
- **WHEN** a run executes
- **THEN** the consumer reads `RunStarted`, text events and exactly one terminal event, in order, and the sequence passes the event sequence rules

#### Scenario: A failing provider still completes the stream
- **GIVEN** a provider that throws during the model call
- **WHEN** the run executes
- **THEN** `RunError` is emitted and the stream completes

#### Scenario: Cancellation ends with a terminal event
- **GIVEN** a run in progress
- **WHEN** the caller cancels the run's token
- **THEN** the stream delivers `RunFinished(cancelled)` and completes

#### Scenario: Abandoning the stream stops the run
- **GIVEN** a consumer that stops reading mid-run
- **WHEN** the enumerator is disposed
- **THEN** the run stops and no further model or tool calls happen

#### Scenario: Overlapping runs are rejected
- **GIVEN** a run already in progress on a harness
- **WHEN** `RunAsync` is called again
- **THEN** it fails with an actionable error naming the sequential-runs rule

### Requirement: Turn semantics
A turn SHALL: append the user message and emit `RunStarted`; build the request from the optional system prompt, the history and the registry's tool declarations; stream text as events; treat function calls as complete (the factory pipeline's accumulator; the harness expects a pipeline-wrapped client); append the assistant message; with no calls, emit `RunFinished` with the mapped stop reason. With calls, each call SHALL emit `ToolCallStart`, `ToolCallArgs` (the complete arguments JSON, or the raw argument text when assembly failed), `ToolCallEnd`, then either execute the tool and emit `ToolCallResult` or, for arguments that cannot be assembled, emit an error `ToolCallResult` — appending the tool message in both cases — and the loop SHALL continue until a model answer has no calls or `MaxSteps` (default 50) is reached. Transient provider failures SHALL retry up to the configured limit with backoff, emitting `Retrying` per attempt, before the turn fails; only attempts that emitted no events retry. Provider usage reports SHALL emit `UsageUpdated`. When a session is attached to the harness options, every message the loop appends to the history SHALL also be appended to the session — the user message, each assistant message with model and usage when known, and each tool result including repaired synthetic ones; a resumed session SHALL seed the history before the run.

#### Scenario: One tool call then a final answer
- **GIVEN** a scripted provider that calls a tool once and then answers
- **WHEN** the run executes
- **THEN** the tool events bracket the execution, the tool message enters the history, and the run ends with `RunFinished(stop)`

#### Scenario: Step limit ends the run
- **GIVEN** a provider that keeps calling tools
- **WHEN** the run reaches `MaxSteps`
- **THEN** `StepLimitReached` is emitted, followed by `RunFinished(step_limit)`

#### Scenario: A transient failure retries and succeeds
- **GIVEN** a provider that fails transiently once and then answers
- **WHEN** the run executes
- **THEN** `Retrying` is emitted for the attempt and the run ends with `RunFinished(stop)`

#### Scenario: Exhausted retries fail the run
- **GIVEN** a provider that keeps failing transiently
- **WHEN** the retry limit is exhausted
- **THEN** `RunError` ends the run with the provider's message

#### Scenario: A sessioned run mirrors the conversation
- **GIVEN** a harness with an attached session
- **WHEN** a scripted run with a tool call executes
- **THEN** the session file contains the user, assistant and tool message entries in order with the parent chain, and a resumed session's first request contains the loaded history

### Requirement: Tool execution safety

Unknown tools, malformed arguments and exceptions from a tool SHALL become error results with instructing messages, never exceptions out of the stream. Arguments that cannot be assembled — a call still carrying raw `$arguments` fragments or a failed assembly, from the pipeline's accumulator or from a raw streaming client — SHALL never reach a tool: `ToolCallArgs` SHALL carry the raw argument text when it exists, falling back to the failure message otherwise, and the result SHALL instruct the model to fix the arguments. Tool output SHALL be truncated to the configured budget before it enters the history; `Details` SHALL remain UI-only; a decline from the approver SHALL become an error result, and an approver that fails SHALL produce an approval-specific error result, not one blaming the tool. The approval seam SHALL carry the asking run (run id, parent run id, depth) so prompts can label a subagent as the asker, and "always this session" decisions SHALL apply across the whole run tree of the session. The event sink passed to tools SHALL accept only extension events; anything else SHALL be rejected and surface as a tool error.

#### Scenario: Tool failure keeps the history valid
- **GIVEN** a scripted tool that throws
- **WHEN** the run executes
- **THEN** the model receives an error result naming what failed, and the run can continue

#### Scenario: Unassembled arguments never reach a tool
- **GIVEN** a call whose arguments are raw fragments or failed to assemble, from the pipeline's accumulator or from a raw streaming client
- **WHEN** the run executes
- **THEN** `ToolCallArgs` carries the raw argument text, no tool executes, and the model receives an instructing error result

#### Scenario: Long output is bounded
- **GIVEN** a tool whose output exceeds the budget
- **WHEN** its result enters the history
- **THEN** the model-facing output carries the truncation marker and the full output stays out of the model's history

#### Scenario: Approver failure is reported as an approval error
- **GIVEN** an approver that throws
- **WHEN** the tool call is considered
- **THEN** the error result names the approval failure, not a tool failure

#### Scenario: Tools cannot emit loop events
- **GIVEN** a tool that emits a non-extension event through its context
- **WHEN** it runs
- **THEN** the emission is rejected and the tool call becomes an error result

### Requirement: Run and tool spans
The loop SHALL emit one run span (`invoke_agent`) per run and one child span (`execute_tool`) per tool execution, carrying the pinned OpenTelemetry GenAI attributes plus `lunate.tool.is_error`; model-call spans SHALL nest under the run span. Without a listener, no spans SHALL be produced.

#### Scenario: A replayed session produces nested spans
- **GIVEN** an in-memory exporter and a replayed session with a tool call
- **WHEN** the run executes
- **THEN** one run span exists with a nested model span and a nested tool span carrying the tool name and call id

### Requirement: Cancellation and history repair
When a run is cancelled or fails after the assistant message was appended, the loop SHALL repair the history before the run ends: every function call in the last assistant message without a result SHALL get a synthetic error result — "cancelled by the user" on cancellation, a failure note on the error path — so the next run on the harness sends a valid history. A call already in flight SHALL also emit a synthetic `ToolCallResult` event so the event sequence stays valid. Cancellation SHALL end with `RunFinished(cancelled)` once `RunStarted` was emitted.

#### Scenario: A run cancelled mid-tool leaves a valid history
- **GIVEN** a run cancelled while a tool is executing
- **WHEN** the next run starts
- **THEN** the provider receives the assistant message followed by one synthetic cancelled result per dangling call

#### Scenario: A failure with pending calls repairs defensively
- **GIVEN** a failure that escapes while calls are still pending (an invariant break; tool failures normally become error results)
- **WHEN** the run ends with `RunError`
- **THEN** every pending call has a synthetic failure result and the next run sends a valid history

### Requirement: Nested calls in the loop
The loop SHALL execute nested tool calls (through `ToolContext.ExecuteToolAsync`) on the same ordered path as top-level calls: same approval, same cancellation token, same event stream. Nested results SHALL NOT enter the conversation history — only the outer call's result does. The depth cap (`MaxNestedToolDepth`) and the exposure gate SHALL be enforced by the loop. User cancellation SHALL propagate out of nested calls; every other failure SHALL become an error result.

#### Scenario: Nested work leaves history to the outer call
- **GIVEN** a run where a tool makes nested calls
- **WHEN** the run completes
- **THEN** the history contains the outer call's single result and no nested entries

#### Scenario: Cancellation propagates through nesting
- **GIVEN** a run cancelled while a nested call is in flight
- **WHEN** the cancellation reaches the nested call
- **THEN** the run stops with the cancellation semantics of T-10 and the history stays valid

### Requirement: Compaction
The loop SHALL compact the request before a model call when the estimated request size passes 80% of the model's context window — estimated as `chars / 4`, corrected by the last reported input-token usage when available — or when compaction is requested explicitly. Compaction SHALL summarize the history older than the kept tail (system prompt, AGENTS.md content, and the last N turns, N configurable, default 4) with the same model and a fixed summarization prompt covering goal, decisions, files touched and open problems. A tool call SHALL never be split from its result — the tail boundary SHALL expand to pair boundaries. The session file SHALL keep the full history; compaction SHALL be recorded as a `compaction` entry (`summary`, `replaces` = the replaced entry ids); in-run compaction SHALL emit `CompactionApplied` with the run id, replaced entry ids and the estimated tokens after compaction (an explicitly requested out-of-run compaction records the entry and rebuilds history — there is no run to carry an event). The rebuilt request SHALL be the system prompt plus the summary plus the kept tail. Compactor failure SHALL leave the request unchanged and SHALL be reported (the next request may retry). When the model window is unknown, a documented default SHALL apply and the run SHALL proceed. An image part SHALL count as `max(256, width × height / 750)` estimated tokens for the trigger, SHALL render as its `[image <w>x<h>]` placeholder in the summarization transcript, and SHALL NOT send its bytes to the summarizer. Compaction SHALL NOT delete image blobs.

#### Scenario: A long session compacts once
- **GIVEN** a recorded long session that passes the threshold
- **WHEN** the next model request is composed
- **THEN** it compacts exactly once, the next request is under 60% of the window, a `compaction` entry records the summary and replaced ids, `CompactionApplied` is emitted, and replay still matches

#### Scenario: Tool pairs are never split
- **GIVEN** a tail boundary falling between a tool call and its result
- **WHEN** compaction runs
- **THEN** the boundary expands so the pair stays together in the kept tail

#### Scenario: Compactor failure leaves the request unchanged
- **GIVEN** a summarization call that fails
- **WHEN** compaction is attempted
- **THEN** the request is sent unchanged and the failure is reported

#### Scenario: Resume after compaction
- **GIVEN** a session file containing a compaction entry
- **WHEN** the session is loaded and its history reconstructed
- **THEN** the history is the summary plus the messages after the replaced entries

#### Scenario: Images count toward the trigger

- **GIVEN** a history with images whose placeholder text alone is small
- **WHEN** the request size is estimated
- **THEN** each image adds `max(256, width × height / 750)` tokens and compaction triggers on that estimate

#### Scenario: The summarizer never receives image bytes

- **GIVEN** a history containing an image part
- **WHEN** the summarization request is built
- **THEN** the transcript carries `[image <w>x<h>]` and no image data

### Requirement: System prompt and project instructions

The agent SHALL compose its system prompt from an embedded `system-prompt.md` template: identity, the available tools by name, and the working rules (read before edit, verify after change). The composer SHALL append the runtime facts (OS, resolved shell, canonical working directory, UTC date) and the contents of every `AGENTS.md` found from the repository root (worktree root when there is no repository) down to the working directory, in root-to-leaf order, each block introduced by its path. When `LUNATE_DATE` is set to an ISO `yyyy-MM-dd` date, the composer SHALL use it as the prompt's UTC date; an invalid value SHALL fail with an error naming the variable and the expected format. The composed prompt SHALL stay under 1,000 tokens (asserted with a documented heuristic), excluding user AGENTS.md content. The composed prompt SHALL be delivered through the harness system prompt so that it leads the model request as its system message.

#### Scenario: Composed prompt leads the request

- **GIVEN** a harness configured with the composed prompt and a fake model client
- **WHEN** a run starts
- **THEN** the request's first message is the composed system prompt

#### Scenario: AGENTS.md chain is appended root to leaf

- **GIVEN** a repository with `AGENTS.md` at the root and in a nested directory, running in that directory
- **WHEN** the prompt is composed
- **THEN** both files appear, root first, each under its relative path

#### Scenario: Budget holds with an empty chain

- **GIVEN** no `AGENTS.md` files
- **WHEN** the prompt is composed
- **THEN** it measures under 1,000 tokens by the documented estimator

#### Scenario: Pinned date for deterministic sessions

- **GIVEN** `LUNATE_DATE=2026-10-18`
- **WHEN** the prompt is composed
- **THEN** the prompt carries `2026-10-18` as its UTC date

#### Scenario: Invalid date pin fails actionably

- **GIVEN** `LUNATE_DATE` set to a non-date value
- **WHEN** the prompt is composed
- **THEN** composition fails with an error naming `LUNATE_DATE` and the expected format

### Requirement: Steering injection

The harness SHALL accept steering messages through a queue on its options while a run is active. The loop SHALL drain the queue before each model request, only after every tool result of the batch has been appended — never between a tool call and its result. Each drained message SHALL be appended to the history and, when a session is attached, to the session, then announced by a `SteeringInjected` event carrying the run id and the entry id (null without a session). Only the top-level run drains steering; leftover messages stay queued and the frontend may reclaim them.

#### Scenario: Steering lands after the batch, never inside it

- **GIVEN** a step with multiple tool calls and a queued steering message
- **WHEN** the loop processes the batch
- **THEN** every call/result pair is appended adjacently, and the steering message is appended after the batch's last result and before the next assistant message

#### Scenario: Leftover steering stays queued

- **GIVEN** a queued steering message that no model request follows
- **WHEN** the run ends
- **THEN** the message remains in the queue, reclaimable by the frontend, and no run starts automatically

#### Scenario: Nested calls never steer

- **GIVEN** a queued steering message and a nested tool call
- **WHEN** the nested call runs
- **THEN** the queue is not drained by it

### Requirement: Subagent runs

`AgentHarness` SHALL stamp a per-run subagent seam into every `ToolContext` it builds: `ToolContext.Subagents`, an `ISubagentRunner` whose `RunAsync(SubagentRequest request, CancellationToken ct)` starts a child run on the request's prompt and returns a `SubagentResult` (child session id when the parent has a session, child run id, output text, stop reason, error flag, tokens used); `Depth` SHALL report the run's spawn depth (0 for a top-level run). The request MAY carry `MaxSteps` and `AllowedTools`, a tool allow-list naming a subset of the run's own offered tools — the child's registry SHALL be exactly that subset (parent order; an unknown name refuses the spawn with a teaching error naming it; null means the parent's full set) — and `WorkspaceRoot`, the child run's worktree root: it SHALL be canonicalized and SHALL be a worktree of the same repository as the parent (the same git-common-dir); null SHALL inherit the parent's workspace, naming the parent's own root SHALL behave as the inherit case, a missing path or non-directory SHALL refuse the spawn with a teaching error naming the path, and a root outside the repository SHALL refuse with its own teaching error. Per-spawn values SHALL only tighten the resolved limits (values above the ceiling are clamped, never loosened). The child run SHALL execute on a child `AgentHarness` composed from the parent's live facts — same model client, hooks, mutation queue, file-change sink and approver — with the working directory and the offered tool set following the request's workspace: without a workspace root (or with the parent's own) the child inherits the parent's working directory and the parent's offered tools as allowed; with another root the child runs in that root and its offered tools SHALL be built for it by the composer's workspace-tool factory — absent that factory the spawn SHALL refuse with a teaching error, never run with the parent's tools in the wrong directory — and an allow-list SHALL name a subset of that built set by the same subset rules. Project MCP tools SHALL NOT be offered to a child running in another root unless their server is explicitly marked workspace-independent (their servers run in the parent's directory); the child SHALL use the parent session's loaded extensions as-is — no per-worktree rediscovery and no trust prompts mid-run. The child runs with the parent's system prompt, the request's prompt as user input and its own run id. The child's events SHALL be forwarded onto the parent's event stream with `ParentRunId` set to the parent run id, their own run id and session id intact, and call ids not rewritten; consumers of a stream SHALL scope events by run id, and hook payloads fired during a child run SHALL carry the child's run id and the parent run id. When the parent has a session, a child session file SHALL be created beside the parent's and `AppendChildSession(childSessionId, runId)` SHALL be appended before the child run starts; without a parent session the child still runs and the child session id is null. The child run's cancellation token SHALL be linked to the caller's token — cancelling the call or the parent run cancels the child (it stops `cancelled`) — and `RunAsync` SHALL join the child before returning. The child's tool approvals SHALL go through the parent's approver, which SHALL identify the asking run (run id, parent run id, depth, and the asking run's workspace root so a prompt can show where the call will act), never bypass it, and a session-wide "always" grant SHALL apply across the whole run tree. Core SHALL enforce, as **teaching error results** naming the limit (and its setting key where the limit is a setting), never exceptions: **depth** — fixed at 1 in this version (a child run's runner refuses every spawn, `depth_limit`; configurable depth arrives later behind a hard ceiling); **concurrency** — `subagents.concurrency` (default 4), tree-wide across every live child run of one top-level run, refused immediately while the limit is held (`concurrency_limit`); **token budget** — `subagents.tokenBudget` (default 100 000 cumulative input+output tokens per child run) and `subagents.treeTokenBudget` (default 500 000 per top-level run tree), checked before each model call (the tighter remaining budget applies; the child stops with `token_limit` and returns its partial result; a spawn with no tree budget left is refused). Limits SHALL resolve from settings (the `subagents` section; `unlimited` only as the explicit literal value) and lower-trust sources — per-spawn requests, and project-scoped overrides when a project settings layer exists — SHALL only tighten them. `SubagentResult.StopReason` SHALL use the run vocabulary (`stop`, `cancelled`, `step_limit`, `length`, `token_limit`) plus the runner refusals (`depth_limit`, `concurrency_limit`, `token_limit`, `workspace_missing`, `workspace_outside_repository`, `workspace_tools_unavailable`) and `error` for a child run that ended in a run error; `IsError` SHALL be true for everything except `stop` and `length`. The child's usage SHALL roll up to the parent run's accounting, and the child session file SHALL keep the child's own usage lines.

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
- **THEN** it returns a `workspace_tools_unavailable` teaching error and no child run starts; a missing or non-directory root returns a `workspace_missing` teaching error naming the path, and a root that is not a worktree of the parent's repository returns a `workspace_outside_repository` teaching error

#### Scenario: A workspace child's ask is labelled with its root

- **GIVEN** a child running in another worktree whose tool call needs approval
- **WHEN** the parent's approver receives the ask
- **THEN** the request carries the child's run id, parent run id, depth and workspace root, and the prompt shows where the call will act

### Requirement: Image parts in model requests

The harness SHALL store every image attachment through the session's image store and SHALL append a session message whose image parts are `ImageContentReference` values (digest, mime, width, height), never image bytes. Before each model request it SHALL hydrate those references into Microsoft.Extensions.AI `DataContent` parts for the provider, reading the blob from the session store or the in-memory store of a no-session run. A tool result carrying `Images` SHALL append its tool-role message with the placeholder text and then one user-role message carrying the image contents with the text `Image from <tool>: [image <w>x<h>].`; the follow-up user message is the portable carrier because the OpenAI chat-completions adapter maps image content only in user-role messages, not in tool-role results. A resumed session SHALL hydrate the same way. Hydration SHALL require the active model to declare `supportsVision`; when it does not, the harness SHALL fail the run before the request, naming the model id and the number of images in the request, and SHALL NOT drop them. A missing referenced blob SHALL fail the run before the first request, naming the digest and the path; the request SHALL NOT silently drop the image.

#### Scenario: A read image becomes a tool message and a user image message

- **GIVEN** a `read` call returning `[image 1024x768]` and PNG bytes
- **WHEN** the loop appends the result
- **THEN** the session gains the tool message with the placeholder and one user message whose contents carry the image reference, and the next request carries `DataContent`

#### Scenario: Resume rehydrates the image

- **GIVEN** a schema 3 session referencing a stored blob
- **WHEN** the session is resumed and a request is built
- **THEN** the request carries the image bytes as `DataContent` and the session file still carries only the reference

#### Scenario: Resume on a text-only model fails

- **GIVEN** a schema 3 session referencing a stored blob and a model without vision support
- **WHEN** the session is resumed and a request is built
- **THEN** the run fails naming the model id and the image count before any request, and no request carries the image

#### Scenario: A missing blob stops the run

- **GIVEN** a session referencing a digest whose blob is missing
- **WHEN** the harness is built for the run
- **THEN** it fails naming the digest and expected path before any request

#### Scenario: A no-session run keeps images in memory

- **GIVEN** a `--no-session` run with an attached image
- **WHEN** the request is built
- **THEN** the image reaches the provider from memory and no file is written
