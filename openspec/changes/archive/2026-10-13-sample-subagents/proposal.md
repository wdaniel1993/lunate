# Sample: subagents (T-47, part 2)

## Why

T-47's promise is *"the core ships primitives and is subagent-ready; features ship as extensions"* (`docs/guide.md`). Part 1 (`add-extension-tools`, PR #63) delivered the tool primitive extensions need to *offer* a subagent tool. What is still missing is the **subagent primitive itself**: today `Session.AppendChildSession`, `SessionChildSessionEntry` and `AgentEvent.ParentRunId` exist in the format and the event contract with **no producer** — nothing can run a child harness, link its session, route its approvals, propagate cancellation or enforce limits. This change delivers that primitive in `Lunate.Agent` and proves it with the first sample extension.

Done-when (T-47 row): *parent/child runs, approvals routed to the parent UI, cancel propagated, limits enforced* — proven by a real extension tool through the production composition, the same way T-45/T-46 and part 1 are proven.

## What Changes

- **A per-run subagent seam**: `ToolContext.Subagents` (`ISubagentRunner`) stamped by the harness at run start — the same one-path stamping model as `RunId`, `FileChanges` and `CallId`. No service-registry handle, no new registration surface.
- **The spawn contract**: prompt, optional `MaxSteps`, and an optional **tool allow-list restricted to the run's own tools** (subset only — an unknown name refuses the spawn; null = the parent's full set). Per-spawn values may only tighten limits, never loosen them.
- **A child harness per call**: same model client, tool registry (as allowed), working directory, hooks, mutation queue, file-change sink and approver; the child gets its own run id and its own **child session**, created beside the parent's session file and linked by `AppendChildSession(childSessionId, runId)` **before** the child run starts. **Depth is fixed at 1** in this version — children cannot spawn (configurable depth arrives later, with a hard ceiling, once nested presentation and approvals are designed).
- **Events on the parent stream**: child events are forwarded with `ParentRunId` stamped (child `RunId`/`SessionId` intact, call ids not rewritten); consumers scope by run id. Hook payloads observed during a child run carry the child's run id **and** the parent run id.
- **Frontends stay quiet and honest**: the TUI shows a *subagent running* status and approvals but hides the child text stream (and child tool blocks); ACP **filters child events, never passing them raw**; print mode's human renderer hides child text while the JSONL keeps child events with their run ids.
- **Approvals**: through the parent's approver, **labelled with the asking run**; a session-wide *"always"* grant applies **across the whole run tree**.
- **Limits — core-enforced, configurable, one-way**: concurrency is **tree-wide** (default 4); token budgets are **per child** (default 100 000) and **per top-level run tree** (default 500 000), cumulative input+output, checked **before each model call** — an overrun stops the child with a dedicated `token_limit` reason and returns the **partial result**. Limits resolve from settings (`subagents` section; `unlimited` only as an explicit value); project-scoped overrides (when they exist) and per-spawn requests may only lower them. `/context` shows the resolved limits and current usage.
- **Result and accounting**: the child's result carries final text (normal tool-output truncation when it enters the conversation), stop reason, child session reference and **usage**; usage rolls up to the parent run (footer, JSONL) while the child session file keeps its own usage lines. A replay test proves a parent session with linked children replays deterministically.
- **Sample `subagents`**: one extension tool (`subagents__spawn`) that runs a child agent through the primitive, with tests proving the behaviours end to end.

## Not in Scope

- **Worktrees per subagent** — that is T-51 (`worktree-tasks`, depends on T-47); this change keeps one workspace.
- **Model routing per subagent** — T-49 (`model-router`) territory.
- **Out-of-process subagents** — T-52 territory.
- **Configurable depth beyond 1 / rich nested TUI presentation** — deferred until the nested presentation and approval design exists.
- **A product toggle for "spawn asks"** — the sample asks (destructive-capable annotations); a user setting for product use is noted as a follow-up.
- **A project-scoped settings layer** — the one-way clamp rule is specified for it, but today's layers are the user file and the environment.

## Impact

- `Lunate.Agent`: `ISubagentRunner`, `SubagentRequest`, `SubagentResult`, `SubagentRunner`; `ToolContext.Subagents`; the approval seam carrying the asking run; the loop's token-budget check; hook payloads gaining the parent run id; PublicAPI updates.
- `Lunate.Coding`: settings `subagents` section, `/context`, the interactive pump's child-run behaviour, print renderer suppression, ACP filtering.
- `samples/extensions/subagents/**`: the sample extension + tests.
- Specs: `agent-loop` (ADD "Subagent runs"; MODIFIED "Tool execution safety"), `agent-tools` (MODIFIED "Tool contract"), `agent-sessions` (MODIFIED "Session store and resume"), `agent-events` (MODIFIED "Closed event set"), `extensions` (MODIFIED hooks-fire requirement), `tui` (MODIFIED "Tool block rendering", "Approval prompt", "Status footer"), `protocols` (MODIFIED "ACP server over stdio").
- No new packages. `Lunate.Extensibility.Abstractions` changes additively only (API 1.7 → 1.8: the parent run id on the run-scoped hook payloads) — extension code reaches the runner through the `Lunate.Agent` reference established in part 1.
