## Why

ADR-0017: the formats must carry extension metadata now, while migrations are still cheap. This is the events-and-tools half of Part A; the session half is the next change (`add-session-schema-v2`, with ADR-0018 for the golden change).

## What Changes

- **Events** (`agent-events`): every event gains optional `SessionId`, `ParentRunId` and `Source` (init properties on the base — no constructor churn); the four tool events gain `ParentToolCallId`; nested calls use ids `"<parent>/<n>"`; a `ToolProgressUpdate` event carries streaming tool output; the harness stamps `SessionId` centrally when a session is attached; consumers must ignore unknown event kinds; usage from nested work rolls up to the owning run.
- **Tools** (`agent-tools`): `ITool` gains `Exposure` (Direct | ModelOnly | Programmatic | Deferred | Hidden), `Namespace`, `Annotations` (ReadOnly | Destructive | Idempotent | OpenWorld), optional `OutputSchema` and per-tool `Concurrency` — all as default interface members; `ToolRisk` stays an explicit override and derives from annotations otherwise. `ToolResult` gains `StructuredContent` and `Usage`. `ToolContext` gains `RunId`, `CallId`, `ExecuteToolAsync` (nested calls: same validation, approval and cancellation; exposure gate; depth cap 5; events with parent ids; no history append), `Progress` and `FileMutations`.
- **File mutation queue** (`agent-tools`): `IFileMutationQueue` (per-path, read-modify-write as one unit; shared default) — `write` and `edit` route their read-modify-write through it.
- **Registry**: `Declarations` exposes only `Direct` and `ModelOnly` tools to the model.
- Out of scope: hooks (T-37), model registry and usage charging for nested model calls (T-39), UI (T-40), session schema v2 (`add-session-schema-v2`).

## Capabilities

### New Capabilities
- None.

### Modified Capabilities
- `agent-events`: event identity/source fields, progress event, unknown-kind tolerance, usage roll-up.
- `agent-tools`: exposure, namespaces, annotations, output schema, concurrency, nested execution, the file mutation queue, registry filtering.
- `agent-loop`: nested tool execution semantics (same approval path, parent ids, depth cap, no history append) and central session-id stamping.

## Impact

- `src/Lunate.Agent/`: `AgentEvent.cs`, `ITool.cs`, `ToolResult.cs`, `ToolContext.cs`, `ToolRegistry.cs`, `AgentEventChannel.cs`, `AgentHarness.cs`/`AgentHarness.Tools.cs`, `AgentHarnessOptions.cs`, new `ToolExposure.cs`, `ToolNamespace.cs`, `ToolAnnotations.cs`, `ToolConcurrency.cs`, `ToolRiskResolver.cs`, `IFileMutationQueue.cs`, `FileMutationQueue.cs` (+ PublicAPI.Unshipped). `src/Lunate.Coding/`: `WriteTool.cs`, `EditTool.cs` route through the queue. Tests in both suites. No new packages.
