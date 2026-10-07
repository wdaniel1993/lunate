## ADDED Requirements

### Requirement: Tool exposure and registry declarations
`ITool` SHALL gain an `Exposure` (`Direct`, `ModelOnly`, `Programmatic`, `Deferred`, `Hidden`, default `Direct`) as a default interface member. `ToolRegistry.Declarations` SHALL expose only `Direct` and `ModelOnly` tools to the model. Nested/programmatic execution SHALL allow `Direct` and `Programmatic` tools and refuse `ModelOnly` and `Hidden` with an instructing error.

#### Scenario: Hidden and programmatic tools are not declared
- **GIVEN** registered tools with exposures `Direct`, `ModelOnly`, `Programmatic` and `Hidden`
- **WHEN** declarations are built
- **THEN** only `Direct` and `ModelOnly` appear

#### Scenario: The programmatic gate refuses model-only tools
- **GIVEN** a tool with exposure `ModelOnly`
- **WHEN** it is called through `ToolContext.ExecuteToolAsync`
- **THEN** the call is refused with an error naming the exposure

### Requirement: Tool namespaces and annotations
`ITool` SHALL gain optional `Namespace` (`Name`, `Description`, `Instructions`) and `Annotations` (`ReadOnly`, `Destructive`, `Idempotent`, `OpenWorld`; all default false) as default interface members. `ToolRisk` SHALL stay an explicit override; without one it SHALL derive from annotations: `ReadOnly` → `ReadOnly`, otherwise `Write`.

#### Scenario: Risk derives from annotations
- **GIVEN** a tool without an explicit risk that declares `ReadOnly`
- **WHEN** its risk is read
- **THEN** it is `ReadOnly`

#### Scenario: An explicit risk wins
- **GIVEN** a tool with an explicit `Execute` risk and `ReadOnly` annotations
- **WHEN** its risk is read
- **THEN** it is `Execute`

### Requirement: Output schema and structured results
`ITool` SHALL gain an optional `OutputSchema`, and `ToolResult` SHALL gain optional `StructuredContent` and `Usage` members. `Details` stays UI-only; `StructuredContent` travels only where the loop sends it (never as raw model text in v1).

#### Scenario: A tool returns structured content
- **GIVEN** a tool with an `OutputSchema` that sets `StructuredContent`
- **WHEN** it runs
- **THEN** the result carries the structured content next to the output text

### Requirement: Per-tool concurrency
`ITool` SHALL gain a `Concurrency` member (`Parallel`, default, or `Sequential`) pinning the tool's scheduling intent for the future parallel executor.

#### Scenario: The default is parallel
- **GIVEN** a tool without a concurrency declaration
- **WHEN** its concurrency is read
- **THEN** it is `Parallel`

### Requirement: File mutation queue
`IFileMutationQueue` SHALL serialize read-modify-write mutations per canonical path (`RunAsync(path, mutation, ct)`), with a process-wide `FileMutationQueue.Shared` default and per-platform path comparison. `write` and `edit` SHALL run their read-modify-write inside the queue.

#### Scenario: Concurrent edits on one file both apply
- **GIVEN** two edits with different, unique `old_text`s on the same file, started concurrently
- **WHEN** both complete
- **THEN** both changes are present (serialized, no lost update)

#### Scenario: Different files do not block each other
- **GIVEN** mutations on two different paths
- **WHEN** they run concurrently
- **THEN** neither waits for the other

### Requirement: Nested tool execution
`ToolContext` SHALL gain `RunId`, `CallId`, `ExecuteToolAsync(name, args, ct, onUpdate)` and `Progress`. Nested calls SHALL run through the same registry, argument validation, approval and cancellation as top-level calls; SHALL emit tool events with the nested call id and `ParentToolCallId`; SHALL NOT append to the conversation history; SHALL be refused beyond `MaxNestedToolDepth` (default 5) with an error naming the cap; and SHALL return failures as error results, never exceptions (user cancellation propagates).

#### Scenario: A nested call runs and is observable
- **GIVEN** a tool that calls `ctx.ExecuteToolAsync` for a registered tool
- **WHEN** the run executes
- **THEN** the nested tool runs, its events carry the nested and parent call ids, and the conversation history contains only the outer call's result

#### Scenario: The depth cap is enforced
- **GIVEN** nested calls beyond the configured depth
- **WHEN** the limit is reached
- **THEN** the call is refused with an error naming the cap

#### Scenario: Nested approval is the same approval
- **GIVEN** an approver that denies the nested tool
- **WHEN** the nested call runs
- **THEN** it returns the denial error result and the tool is not executed
