# Tasks — add-extension-formats

## 1. Events (TDD)

- [x] 1.1 Base init properties `SessionId`, `ParentRunId`, `Source`; `ParentToolCallId` on the four tool events; `ToolProgressUpdate` event
- [x] 1.2 `AgentEventChannel` stamps `SessionId` when a session is attached (constructor takes the optional id); harness passes it
- [ ] 1.3 Tests: stamping on every event of a session-attached run; null when detached; progress event emitted from `ctx.Progress`; a small consumer helper proving unknown kinds are ignored

## 2. Tool contract (TDD)

- [x] 2.1 New types: `ToolExposure`, `ToolNamespace`, `ToolAnnotations`, `ToolConcurrency`, `ToolRiskResolver`, `IFileMutationQueue` + `FileMutationQueue` (per-path serialization, shared default, platform case rule)
- [x] 2.2 `ITool` default interface members (exposure, namespace, annotations, output schema, concurrency, derived risk); explicit `Risk` overrides on existing tools unchanged
- [x] 2.3 `ToolResult` gains `StructuredContent` and `Usage`; `ToolRegistry.Declarations` filters to `Direct`/`ModelOnly`
- [x] 2.4 Tests: declaration filter; derived vs explicit risk; result shape; queue concurrency (same path serializes with no lost update, different paths independent)

## 3. Nested execution and queue routing (TDD)

- [ ] 3.1 `ToolContext` gains `RunId`, `CallId`, `ExecuteToolAsync`, `Progress`, `FileMutations`; harness fills them
- [ ] 3.2 `RunNestedToolAsync`: nested ids `<parent>/<n>`, exposure gate, depth cap (`MaxNestedToolDepth`, default 5), same approval, events with parent ids, no history append, error results (cancellation propagates)
- [ ] 3.3 `AgentHarnessOptions`: `MaxNestedToolDepth` (validated ≥ 1), `FileMutations` (default shared)
- [ ] 3.4 `WriteTool`/`EditTool` route their read-modify-write through the queue (optional constructor parameter, shared default)
- [ ] 3.5 Tests: nested success with parent ids on events; depth cap; exposure refusal; approval denial; no history append; cancellation through nesting; concurrent edit/write serialization

## 4. Specs and close

- [ ] 4.1 Spec deltas (written); `PublicAPI.Unshipped.txt` entries for all new public surface
- [ ] 4.2 `dotnet csharpier format .`; `bash scripts/verify.sh` green (incl. de-AT); `openspec validate add-extension-formats --type change --strict`; self-review
