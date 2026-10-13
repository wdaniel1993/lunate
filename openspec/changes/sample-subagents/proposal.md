# Sample: subagents (T-47, part 2)

## Why

T-47's promise is *"the core ships primitives and is subagent-ready; features ship as extensions"* (`docs/guide.md`). Part 1 (`add-extension-tools`, PR #63) delivered the tool primitive extensions need to *offer* a subagent tool. What is still missing is the **subagent primitive itself**: today `Session.AppendChildSession`, `SessionChildSessionEntry` and `AgentEvent.ParentRunId` exist in the format and the event contract with **no producer** — nothing can run a child harness, link its session, route its approvals, propagate cancellation or enforce limits. This change delivers that primitive in `Lunate.Agent` and proves it with the first sample extension.

Done-when (T-47 row): *parent/child runs, approvals routed to the parent UI, cancel propagated, limits enforced* — proven by a real extension tool through the production composition, the same way T-45/T-46 and part 1 are proven.

## What Changes

- **A per-run subagent seam**: `ToolContext.Subagents` (`ISubagentRunner`) stamped by the harness at run start — the same one-path stamping model as `RunId`, `FileChanges` and `CallId` (signed off for the file-change bus). No service-registry handle, no new registration surface.
- **A child harness per call**: same model client, same tool registry, same working directory, same hooks/mutation/change plumbing; the parent's approver inherited; the child gets its own run id and its own **child session**, created beside the parent's session file and linked by `AppendChildSession(childSessionId, runId)` **before** the child run starts.
- **Events on the parent stream**: the child run's events are forwarded onto the parent's stream with `ParentRunId` stamped (child `RunId`/`SessionId` intact); consumers stop assuming one run per stream.
- **Approvals, cancellation, limits — enforced by core**: the child's tool approvals go through the parent's approver (same UI seam); cancellation propagates through a linked token and the child stops `cancelled`; depth, concurrency and token limits are core knobs on `AgentHarnessOptions` with documented defaults; refusals are **teaching error results**, never crashes (ADR-0003).
- **Sample `subagents`**: one extension tool (`subagents__spawn`) that runs a child agent on a prompt through the primitive, with tests proving the four behaviours end to end.

## Not in Scope

- **Worktrees per subagent** — that is T-51 (`worktree-tasks`, depends on T-47); this change keeps one workspace.
- **Model routing per subagent** — T-49 (`model-router`) territory.
- **Out-of-process subagents** — T-52 territory.
- **A full nested rendering mode in the TUI** — the change keeps child output honest (events flow, blocks commit, no single-run assumptions); richer nesting is a follow-up if wanted.

## Impact

- `Lunate.Agent`: `ISubagentRunner`, `SubagentRequest`, `SubagentResult`, `SubagentRunner`; `ToolContext.Subagents`; `AgentHarnessOptions` limit knobs; the interactive pump's run-scoped keying (Coding); PublicAPI updates.
- `samples/extensions/subagents/**`: the sample extension + tests.
- Specs: `agent-loop` (ADD "Subagent runs"), `agent-tools` (MODIFIED "Tool contract"), `agent-sessions` (MODIFIED "Session store and resume"), `agent-events` (MODIFIED "Closed event set" — `ParentRunId` semantics on forwarded child events).
- No new packages. `Lunate.Extensibility.Abstractions` unchanged (API stays 1.7) — extension code reaches the runner through the `Lunate.Agent` reference established in part 1.
