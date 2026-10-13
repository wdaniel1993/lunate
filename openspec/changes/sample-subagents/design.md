# Design — sample-subagents

Sign-off (Daniel, 2026-10-13): D1 yes; D2 with a tool allow-list + hook `ParentRunId`; D5 with asking-run labels + tree-wide "always"; D7 reworked (depth fixed 1, tree-wide concurrency, per-child + per-tree budgets, settings, one-way clamps); added contracts (result/usage/roll-up, frontend behaviour, replay). Applied below.

## D1 — The runner is a per-run seam on `ToolContext`, not a registry service

`ToolContext.Subagents` is an `ISubagentRunner?` init property stamped by the harness in `BuildToolContext`, next to `RunId`, `CallId`, `FileChanges` and `Progress` — one stamping model, no second mechanism. Rationale: the runner needs the *live run facts* (channel, approver, client, registry, depth, tree state); a session-scoped registry handle cannot know them and would need its own attach/detach protocol. The handle types live in `Lunate.Agent` (the contract the extension already references since part 1); `Lunate.Extensibility.Abstractions` and its API version are untouched.

```csharp
public interface ISubagentRunner
{
    int Depth { get; }
    Task<SubagentResult> RunAsync(SubagentRequest request, CancellationToken ct);
}

public sealed record SubagentRequest(
    string Prompt,
    int? MaxSteps = null,
    IReadOnlyList<string>? AllowedTools = null
);

public sealed record SubagentResult(
    string? ChildSessionId,
    string? RunId,
    string Output,
    string StopReason,
    bool IsError,
    long TokensUsed
);
```

`Subagents` is null outside a run (tools executed standalone); the sample treats null as a teaching error result. Refusals (depth/concurrency/token) are **results** with `IsError: true`, the limit named in `Output` **and its setting key** — never exceptions (ADR-0003: the model learns what happened).

## D2 — The child harness and the spawn contract

Constructed per call from the parent's live facts: same `IChatClient`, same tool set (as allowed — below), same `WorkingDirectory`, same `Hooks` (child-run payloads carry the child's run id + parent run id, D4), `FileMutations`, `FileChanges` (the child harness stamps its own run id into its sink as usual), `ToolOutputLimit`, `MaxNestedToolDepth`, `ModelCatalog`/`ModelId`. `Approver` is inherited (D5). `MaxSteps` = `request.MaxSteps` clamped to the resolved ceiling. `SystemPrompt` = the parent's system prompt; the request's `Prompt` is the child run's user input.

**Tool allow-list (sign-off):** `AllowedTools` is a subset of the run's own offered tools, by model-facing name. The child's registry is the named subset in the parent's order; an **unknown name refuses the spawn** (teaching error naming it — the parent can only grant what it has); `null` = the parent's full set (v1 default). The spawning tool itself is only included when named — with depth fixed at 1 it can never spawn anyway (D7).

**Per-spawn tightening:** `MaxSteps` and any future per-spawn knob may only tighten the resolved limits; values above the ceiling are clamped to it (never looser, never an error — clamping is the documented one-way rule).

## D3 — Child session lifecycle

A child session file is created **beside the parent's session file** (same sessions directory — the worktree/repo grouping is preserved), with a fresh id. If the parent harness has no session attached, the child run still runs but no child session is persisted and `ChildSessionId` is null (the no-session case stays useful for tests and embedded use). The parent **appends `AppendChildSession(childSessionId, runId)` before the child run starts**, so a crash mid-child still leaves the link. The entry type, format and serializer already exist; this change adds the producer. Resuming the parent renders the child link as a single informational line; resuming the child loads its own history.

## D4 — Events on the parent stream, hooks, and the run tree

The runner consumes the child's event stream and forwards every event onto the parent's channel with `ParentRunId` = the parent run id stamped (a `with` expression; child `RunId` and the channel-stamped child `SessionId` stay intact). Call ids are **not** rewritten. The contract becomes: *a stream may interleave events of the primary run and child runs; consumers scope by run id.* **Hook payloads fired during a child run carry the child's run id and the parent run id** (sign-off) so handlers can correlate the tree. A regression test uses a scripted client emitting `call_1` in both runs to prove no cross-talk.

**Frontend behaviour (sign-off, minimal):**
- **TUI**: while a child run is live the live area shows a `subagent running` status; the child's text stream and child tool blocks are **hidden** from scrollback; the spawning tool's own block carries the child's final result; child `UsageUpdated` still rolls into the footer (D8).
- **ACP**: child-run events are **never passed raw**; v1 filters them out of `session/update` (approvals surface through the client-backed approver as usual).
- **Print**: the human renderer hides child text the same way; the JSONL keeps child events with their `runId`/`parentRunId` fields (the scripting contract stays complete).
- The interactive pump still scopes its call maps by `(RunId, CallId)` so suppression is a rendering choice, not a correctness crutch.

## D5 — Approvals: parent seam, asking-run labels, tree-wide "always"

The child harness gets the parent's `IToolApprover`, with the **asking run identified on the seam** (run id, parent run id, depth) so the prompt can say who is asking (`subagent (depth 1)`); the base run asks as before. **"Always for this session" applies across the whole run tree** (sign-off, recommended yes): one grant for a tool covers that tool in every run of the tree — the approval memory is session-scoped, not run-scoped, and a child can never widen it beyond what the user granted. Tests: a recording approver sees the child's request with its run context (allow + deny); an "always" granted while a child asked covers the parent and siblings afterwards.

## D6 — Cancellation

The child run's token is linked to the caller's `ct` — which is the spawning tool call's token, itself linked to the run's token. Cancelling the parent run (or the single tool call) cancels the child; the child harness maps it to `RunFinished.StopReason = "cancelled"`; the runner returns a `cancelled` result; the tool call surfaces as a cancelled/error result with part 1's semantics. The runner always **joins** the child (no orphaned runs): the parent's run loop cannot finish while a child is running.

## D7 — Limits (core-enforced, configurable, one-way) — sign-off rework

**Depth: fixed at 1.** `SubagentRunner.Depth` counts spawn nesting; a depth-1 run's runner refuses **every** spawn (`depth_limit`) — children cannot spawn in v1. Not configurable: configurable depth arrives later **with a hard ceiling**, once nested presentation and approvals are designed (D4/D5 carry the reason).

**Concurrency: tree-wide.** `subagents.concurrency` (default **4**) counts every live child run across the **whole tree** of one top-level run (one shared tree state, created at depth 0 and passed down). A spawn while the limit is held is refused **immediately** (`concurrency_limit`) — a call never blocks invisibly.

**Token budgets: per child and per tree.** `subagents.tokenBudget` (default **100 000**, cumulative input+output per child run) and `subagents.treeTokenBudget` (default **500 000** per top-level run tree). The child loop checks the remaining budget **before each model call** and stops with `token_limit` when exhausted, returning the **partial result** text; a spawn with no tree budget left is refused (`token_limit`). The tighter of (child remaining, tree remaining) applies.

**Resolution and one-way clamps.** Limits resolve from settings (user file + environment overrides, the `subagents` section; **`unlimited` only as the explicit literal value**) with the numbers above as defaults. Lower-trust sources may only tighten, never loosen: per-spawn requests clamp to the resolved ceiling, and project-scoped overrides (when a project settings layer exists) follow the same one-way rule. An overrun stop reason **names the limit and the setting key** (teaching output), matching D1.

## D8 — Result, usage roll-up, stop reasons

`SubagentResult`: final text (truncated only by the normal tool-output budget when it enters the conversation), stop reason, child session reference, and `TokensUsed`. **Usage rolls up**: forwarded `UsageUpdated` events reach the parent run's accounting — the TUI footer and the JSONL — while the child session file keeps the child's own usage lines; the tree budget counts the same numbers.

`StopReason` reuses the run vocabulary (`stop`, `cancelled`, `step_limit`, `length`, `token_limit`) plus the runner refusals (`depth_limit`, `concurrency_limit`, `token_limit`). `IsError` is true for everything except `stop` and `length` (a truncated child answer is still an answer).

## D9 — The sample

`samples/extensions/subagents/` — extension id `subagents`, one tool `spawn` registered as `subagents__spawn` (naming from part 1), schema `{"prompt": string, "maxSteps"?: integer, "tools"?: array of string}`, annotations: not read-only, destructive-capable → approvals **ask** (fine for the sample, sign-off; a product-side toggle is a noted follow-up). A setting exposes the optional `maxSteps` default and the tool description. The manifest declares the tool. Tests (own project, mirroring `mcp-from-extension`): registration + manifest; the four behaviours end to end (child events + link + hooks carry `ParentRunId`; approval routing with labels + tree-wide "always"; cancel; depth/concurrency/token refusals + `token_limit` partial result) plus a `stop` happy path with child output returned to the model and usage rolled up.

## D10 — API, settings, `/context`

`Lunate.Agent` public surface grows (runner types incl. the request's allow-list, `ToolContext.Subagents`, the approval seam's asking-run context, the loop's budget check, hook payload parent run id) — `PublicAPI.Unshipped.txt` updated. `Lunate.Coding`: `SettingsStore` gains the `subagents` section (three keys + `unlimited` literal; validation errors collected like the existing keys) and `/context` gains the resolved limits + current tree usage lines. `Lunate.Extensibility.Abstractions` unchanged (1.7). No new packages, no new assemblies, no ALC changes.

## D11 — Docs

`docs/spec/extensibility.md` subagents row: production path = `ToolContext.Subagents` + sample, the four behaviours, limits; `docs/guide.md` T-47 row: part 2 status; settings documented where the existing keys are documented; sample README (build, install, what it proves).

## D12 — Known limits (documented, not hidden)

- Child runs use the parent's options minus the limits above; no separate compaction budget — bounded by `MaxSteps` + the budgets.
- No cross-tree concurrency coordination — the tree cap is per top-level run, as signed off.
- Depth above 1, nested presentation/approvals, a "spawn asks" product toggle, and a project settings layer are explicit non-goals here (proposal's Not in Scope).
- Suppressed child streams mean the TUI shows progress, not the child transcript; the child session file and `/resume` on the child cover inspection.

## D13 — Replay determinism

A parent session with linked child sessions **replays deterministically**: loading the parent produces the same reconstructed messages and the same rendered child-link lines across repeated loads, and loading each child produces exactly the child's own history. The test uses a committed fixture pair of session files and asserts stability (deterministic replay, not wall-clock-dependent output).

## Deviations

- D8's result vocabulary omits two cases; both report `StopReason = "error"` (`StopReasons.Error`) with `IsError: true`: a child run that ends in `RunError`, and a spawn refused for a non-limit reason (an unknown allow-list name, whose teaching error names it). A child error message leads the `Output` so the parent model reads what failed.
- The loop's token-budget check runs after the `MaxSteps` check, so an exhausted step ceiling keeps precedence at the boundary (`step_limit` over `token_limit`).
- Depth refusals name `subagents.depth` as the key although depth is not configurable in this version (fixed at 1); the message says so, keeping "name the limit and its setting key" literal.
- Hook `ParentRunId` lives on the `Lunate.Agent` hook records (`AgentHookRecords`); the extension-facing hook DTOs in `Lunate.Extensibility.Abstractions` stay unchanged per the proposal ("API stays 1.7"), so extension handlers correlate a child run by its run id while the parent run id is available at the harness seam.
- `ProviderStreamEventAsync` keeps its `(string runId, update, ct)` signature — it carries no payload record to extend — so the raw-update hook is the one hook without `ParentRunId`.
- The sample's concurrency-refusal test drives two concurrent spawns through a test probe tool: the loop executes a turn's calls sequentially, so only a programmatic caller can hold the tree concurrency in this version.
- `ExtensionTestHostOptions` (the testing kit, not product API) gained `SubagentLimits` and `ConfigureHarness` so sample tests can pin budgets and observe the harness hook seam.
