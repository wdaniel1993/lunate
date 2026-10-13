# Design — sample-subagents

## D1 — The runner is a per-run seam on `ToolContext`, not a registry service

`ToolContext.Subagents` is an `ISubagentRunner?` init property stamped by the harness in `BuildToolContext`, next to `RunId`, `CallId`, `FileChanges` and `Progress` — one stamping model, no second mechanism. Rationale: the runner needs the *live run facts* (channel, approver, client, registry, depth); a session-scoped registry handle cannot know them and would need its own attach/detach protocol. The handle types live in `Lunate.Agent` (the contract the extension already references since part 1); `Lunate.Extensibility.Abstractions` and its API version are untouched.

```csharp
public interface ISubagentRunner
{
    int Depth { get; }
    Task<SubagentResult> RunAsync(SubagentRequest request, CancellationToken ct);
}

public sealed record SubagentRequest(string Prompt, int? MaxSteps = null);

public sealed record SubagentResult(
    string? ChildSessionId,
    string? RunId,
    string Output,
    string StopReason,
    bool IsError
);
```

`Subagents` is null outside a run (tools executed standalone); the sample treats null as a teaching error result. Refusals (depth/concurrency/token) are **results** with `IsError: true`, the limit named in `Output` and a `StopReason` from D8 — never exceptions (ADR-0003: the model learns what happened).

## D2 — The child harness

Constructed per call from the parent's live facts: same `IChatClient`, same `ToolRegistry` (the child sees the same tools — including the spawning tool, which is what makes the depth limit load-bearing), same `WorkingDirectory`, same `Hooks`, `FileMutations`, `FileChanges` (the child harness stamps its own run id into its sink as usual), `ToolOutputLimit`, `MaxNestedToolDepth`, `ModelCatalog`/`ModelId`. `Approver` is inherited (D5). `MaxSteps` = `request.MaxSteps ?? the spawning call's options value`. `SystemPrompt` = the parent's system prompt; the request's `Prompt` is the child run's user input. `MaxSubagentDepth`/`MaxConcurrentSubagents`/`SubagentTokenBudget` come from the parent's options.

The child session (D3) is the child harness's `Session`, so the child's history is mirrored and resumable exactly like a top-level run's.

## D3 — Child session lifecycle

A child session file is created **beside the parent's session file** (same sessions directory — the worktree/repo grouping is preserved), with a fresh id. If the parent harness has no session attached, the child run still runs but no child session is persisted and `ChildSessionId` is null (the no-session case stays useful for tests and embedded use). The parent **appends `AppendChildSession(childSessionId, runId)` before the child run starts**, so a crash mid-child still leaves the link. The entry type, format and serializer already exist; this change adds the producer. Resuming the parent renders the child link as a single informational line; resuming the child loads its own history (both are session-store behaviours, asserted in tests).

## D4 — Events on the parent stream

The runner consumes the child's event stream and forwards every event onto the parent's channel with `ParentRunId` = the parent run id stamped (a `with` expression; child `RunId` and the channel-stamped child `SessionId` stay intact). Call ids are **not** rewritten. The contract becomes: *a stream may interleave events of the primary run and child runs; consumers scope by run id.* Consequences, asserted by tests:

- The interactive pump keys its call maps by `(RunId, CallId)` and only lets the **primary** run's top-level calls take over the live tool indicator; child tool calls commit their blocks without claiming the footer slot.
- Print mode: the JSONL emits child events with their `runId`/`parentRunId` fields as-is; the human renderer prints child text through the same path as any text.
- ACP: forwards as it receives.
- Two runs with the same provider-generated call id ("call_1") must not cross-talk — the regression test uses a scripted client that produces exactly that.

## D5 — Approvals through the parent seam

The child harness gets the parent's `IToolApprover` unchanged — approvals appear in the same UI as the parent's, and the child's tool calls hit the same policy. Test: a recording approver proves the child's call routed to it (deny → error result; allow → runs).

## D6 — Cancellation

The child run's token is linked to the caller's `ct` — which is the spawning tool call's token, itself linked to the run's token. Cancelling the parent run (or the single tool call) cancels the child; the child harness maps it to `RunFinished.StopReason = "cancelled"`; the runner returns a `cancelled` result; the tool call surfaces as a cancelled/error result with part 1's semantics. The runner always **joins** the child (no orphaned runs): the parent's run loop cannot finish while a child is running.

## D7 — Limits (core-enforced, defaults documented)

On `AgentHarnessOptions`:

| Knob | Default | Enforced as |
| --- | --- | --- |
| `MaxSubagentDepth` | 2 | A call whose child depth would exceed it → refusal result (`depth_limit`). Depth counts spawn nesting: root spawns depth-1 child; that child may spawn depth-2; depth-3 is refused. |
| `MaxConcurrentSubagents` | 4 | Per run; parallel spawning calls beyond it wait or are refused — decision: **refused immediately** with `concurrency_limit` when the limit is already held, so a call never blocks invisibly. |
| `SubagentTokenBudget` | 100 000 | Per child run; the runner sums `UsageUpdated` (input + output) and cancels the child when the budget is exceeded → `token_limit`. |

## D8 — Stop reasons on the result

`SubagentResult.StopReason` reuses the run vocabulary (`stop`, `cancelled`, `step_limit`, `length`) and adds the three runner refusals (`depth_limit`, `concurrency_limit`, `token_limit`). `IsError` is true for everything except `stop` and `length` (a truncated child answer is still an answer).

## D9 — The sample

`samples/extensions/subagents/` — extension id `subagents`, one tool `spawn` registered as `subagents__spawn` (name inherited from part 1: `<extension-id>__<tool>`), schema `{"prompt": string, "maxSteps"?: integer}`, annotations: not read-only (the child can run any tool, including mutating ones), destructive-capable → approvals ask by default, exactly the MCP-defaults rule from part 1. A setting exposes the optional `maxSteps` default and the tool description. The manifest declares the tool (part 1's asymmetry applies). Tests (own project, mirroring `mcp-from-extension`): registration + manifest tests; four behaviour tests — (1) parent stream carries child events with `ParentRunId` and the parent session gained the child link; (2) approval routed to the recording approver; (3) parent cancel → child `cancelled`, no hang; (4) depth / concurrency / token refusals with a scripted client, plus a `stop` happy path with child output returned to the model.

## D10 — API and packaging

`Lunate.Agent` public surface grows (runner types, `ToolContext.Subagents`, three `AgentHarnessOptions` knobs) — `PublicAPI.Unshipped.txt` updated; shipped counts move at release. `Lunate.Extensibility.Abstractions` unchanged (1.7). No new packages, no new assemblies, no ALC changes (the sample loads like every extension).

## D11 — Docs

`docs/spec/extensibility.md` subagents row: production path = `ToolContext.Subagents` + sample (update the fitness table wording); `docs/guide.md` T-47 row: part 2 status; the sample gets a README (build, install, what it proves).

## D12 — Known limits (documented, not hidden)

- Child output is not compacted as its own context budget — the child runs with the parent's options; a runaway child is bounded by `MaxSteps` + the token budget.
- No cross-run tool-concurrency coordination beyond the spawn cap — per-tool concurrency (part of T-38) applies per registry as today.
- The rendered child output stays minimal (blocks commit; no collapsible nesting); a richer TUI presentation is a follow-up card if wanted.
