# Design — add-extension-formats (Part A: events + tools)

## Events (pinned)

- On the abstract base record `AgentEvent(string RunId)` add **init properties** (no constructor churn, additive to every derived record):
  - `public string? SessionId { get; init; }` — stamped centrally by `AgentEventChannel` when the harness runs with a session (channel constructor takes the optional session id; `Emit` does `evt with { SessionId = ... }` when null). Tests: every event of a session-attached run carries the id; detached runs carry null.
  - `public string? ParentRunId { get; init; }` — for child runs (subagents later); always null until then, but the field exists.
  - `public string Source { get; init; } = "core"` — extension-emitted events set their extension id (later); core events keep the default.
- On the four tool events (`ToolCallStart`, `ToolCallArgs`, `ToolCallEnd`, `ToolCallResult`) add `public string? ParentToolCallId { get; init; }` — set for nested calls only.
- New event: `public sealed record ToolProgressUpdate(string RunId, string CallId, string Message) : ExtensionEvent(RunId);`
- **Unknown-kind tolerance** (spec requirement, `agent-events`): consumers (TUI, ACP, print/json) must ignore event kinds they do not know — extension events are `ExtensionEvent` subclasses; no consumer change is needed now, the requirement is pinned for the frontends.
- **Usage roll-up** (spec requirement): usage reported by nested work is charged to the owning run via a `UsageUpdated` event on that run. The requirement lands with T-39 (nested model calls); it is deliberately not in this change's delta.

## Tool contract (pinned)

- `ITool` additions are **default interface members** (existing implementations compile unchanged; new tools opt in):
  - `ToolExposure Exposure => ToolExposure.Direct;`
  - `ToolNamespace? Namespace => null;`
  - `ToolAnnotations? Annotations => null;`
  - `JsonElement? OutputSchema => null;`
  - `ToolConcurrency Concurrency => ToolConcurrency.Parallel;`
  - `ToolRisk Risk => ToolRiskResolver.FromAnnotations(Annotations);` — the explicit override on existing tools stays; a tool without an explicit `Risk` derives from annotations.
- New types:
  - `enum ToolExposure { Direct, ModelOnly, Programmatic, Deferred, Hidden }` — semantics per `docs/spec/extensibility.md`; registry declares `Direct`/`ModelOnly` only; nested/programmatic execution allows `Direct`/`Programmatic` and refuses `ModelOnly`/`Hidden` with an instructing error.
  - `record ToolNamespace(string Name, string? Description = null, string? Instructions = null);`
  - `record ToolAnnotations(bool ReadOnly = false, bool Destructive = false, bool Idempotent = false, bool OpenWorld = false);` (MCP meanings; defaults are all false).
  - `enum ToolConcurrency { Parallel, Sequential }` — consumed by the scheduler when parallel tool execution lands; pinned now.
  - `static class ToolRiskResolver { public static ToolRisk FromAnnotations(ToolAnnotations? annotations) => annotations is { ReadOnly: true } ? ToolRisk.ReadOnly : ToolRisk.Write; }`
- `ToolResult` gains optional members: `ToolResult(string Output, bool IsError, object? Details = null, JsonElement? StructuredContent = null, UsageDetails? Usage = null)` (positional optionals — additive).
- `ToolRegistry.Declarations` filters: only tools with `Exposure` in { `Direct`, `ModelOnly` } are declared to the model. `Tools` (registration list) is unchanged.

## ToolContext and nested execution (pinned)

- `ToolContext` becomes: `record ToolContext(string WorkingDirectory, IAgentEvents Events)` + init properties: `string? RunId`, `string? CallId`, `Func<string, JsonElement, CancellationToken, Task<ToolResult>>? ExecuteToolAsync`, `Action<string>? Progress`, `IFileMutationQueue FileMutations = FileMutationQueue.Shared` (the last as a positional default or init default — implementation detail).
- The harness fills these when executing a call. `ExecuteToolAsync` runs a **nested tool call** through the same pipeline:
  - Same registry lookup, same approval (`IToolApprover`), same cancellation token (the run's), same argument validation; exposure gate as above.
  - Nested call id: `"<parent>/<n>"` with `n` a per-run incrementing counter (culture-invariant).
  - Events: `ToolCallStart/Args/End/Result` emitted with the nested call id and `ParentToolCallId` = the parent call id; result output truncated like top-level.
  - **No history append** — the nested call happens inside a tool; the tool's own result is what enters history.
  - Depth cap: `AgentHarnessOptions.MaxNestedToolDepth` (default **5**); exceeding it returns an error result naming the cap. A nested failure returns an error `ToolResult`, never an exception (except user cancellation, which propagates).
  - Hooks integrate at T-37; until then "same pipeline" means validation, approval, events, cancellation.
- `Progress` emits `ToolProgressUpdate` on the run's channel with the current call id.

## File mutation queue (pinned)

- `interface IFileMutationQueue { Task<T> RunAsync<T>(string path, Func<CancellationToken, Task<T>> mutation, CancellationToken ct); }`
- `FileMutationQueue` (sealed): per-path serialization via `SemaphoreSlim` per canonical path; comparer follows the platform case rule (OrdinalIgnoreCase on Windows/macOS, Ordinal on Linux); `public static FileMutationQueue Shared { get; }` is the process-wide default.
- `WriteTool` and `EditTool` gain an optional `IFileMutationQueue` constructor parameter defaulting to `FileMutationQueue.Shared`, and run their whole read-modify-write inside `RunAsync` (existence check + write for `write`; read + match + write for `edit`). Tests: two concurrent edits on the same file both apply (serialized, no lost update); different files are not blocked by one another.

## Harness changes

- `AgentHarnessOptions`: `MaxNestedToolDepth` (int, default 5, validated ≥ 1), `FileMutations` (`IFileMutationQueue`, default shared).
- `AgentHarness.Tools.cs`: `RunToolAsync` gains a nested variant `RunNestedToolAsync(runId, parentCallId, name, args, channel, depth, ct)`; approval + execution are factored so both paths share them; the context builder fills the new members. Depth is carried in the closure, not ambient state.
- `AgentEventChannel`: optional session id in the constructor; stamps `SessionId` on every emitted event.

## Spec deltas

- `agent-events`: new requirement "Event identity and source" (SessionId/ParentRunId/Source stamping, ParentToolCallId, nested id shape), "Tool progress events", "Unknown event kinds are ignored", "Usage roll-up" (requirement only).
- `agent-tools`: new requirements "Tool exposure and registry declarations", "Tool namespaces and annotations", "Output schema and structured results", "Per-tool concurrency", "File mutation queue", "Nested tool execution".
- `agent-loop`: nested execution shares the approval path; depth cap; no history append for nested calls; session-id stamping of events.

## Testing strategy

- Scripted-harness tests with a test tool that calls `ctx.ExecuteToolAsync` (success, depth cap, exposure refusal, approval denial, cancellation, parent ids on events, no history append).
- Registry declaration filter tests; risk-derivation tests; `ToolResult`/`ToolContext` shape tests.
- Queue: concurrency tests with `Task.WhenAll` on same/different paths.
- Session stamping: run with a session → all events carry its id.
- Culture: ids/numbers invariant; de-AT pass in the gate.

## Deliberate non-goals

- Hooks (T-37); model registry/usage charging (T-39); UI consumption of progress (T-40); session schema v2 (next change); per-tool `Sequential` scheduling (scheduler lands with parallel execution later — the property is pinned now).
