# agent-tools Specification

## Purpose
The tool surface of the agent: what a tool is (our own contract with risk levels, working directory and events), how it is declared to the model without Microsoft.Extensions.AI ever invoking it, how tools are registered and found by name, and how tool output is bounded before it enters the conversation.

## Requirements

### Requirement: Tool contract
`Lunate.Agent` SHALL define tools as its own type: `ITool` with a name, a description, a hand-written JSON `ParametersSchema`, a `ToolRisk` (`ReadOnly`, `Write`, `Execute`) and `ExecuteAsync(JsonElement args, ToolContext ctx, CancellationToken ct)`; results as `ToolResult` (output, error flag, UI-only `Details`); and `ToolContext` carrying the working directory and an event sink that accepts only extension events. Schemas SHALL be authored by hand, never built by reflection. Error results SHALL reach the model through their instructing output text; a provider-level error flag is not sent (revisit when real tools and a live fixture exist).

#### Scenario: Contract is implementable without reflection
- **GIVEN** a tool implemented in a test with a hand-written schema
- **WHEN** it is exercised through the contract types
- **THEN** no reflection is involved and the result carries output, error flag and optional UI-only details

#### Scenario: Details never travel to the model
- **GIVEN** a tool result with `Details` set
- **WHEN** the loop appends the tool result to the conversation
- **THEN** only the output reaches the model — instructing text for error results — while details stay UI-only and the error flag feeds events and telemetry

### Requirement: Declaration-only model adapter
A `ToolDeclaration` SHALL present an `ITool` to Microsoft.Extensions.AI as an `AIFunction` whose name, description and JSON schema are exactly the tool's own, and SHALL refuse execution through Microsoft.Extensions.AI on every invocation path, naming ADR-0003.

#### Scenario: Schema reaches the model unchanged
- **GIVEN** a tool with a schema authored in a particular key order and formatting
- **WHEN** the declaration exposes it
- **THEN** the schema is byte-identical to the authored raw JSON text

#### Scenario: Invocation through Microsoft.Extensions.AI is refused
- **GIVEN** a declaration adapter
- **WHEN** any Microsoft.Extensions.AI invocation path is called
- **THEN** it throws `NotSupportedException` naming ADR-0003

### Requirement: Tool registry
A `ToolRegistry` SHALL hold tools by name in registration order, SHALL reject duplicate names at registration, SHALL return null for unknown names, and SHALL expose the declaration adapters for building `ChatOptions`.

#### Scenario: Registration and lookup
- **GIVEN** two registered tools
- **WHEN** they are looked up by name
- **THEN** each returns its tool, an unknown name returns null, and the declaration list matches registration order

#### Scenario: Duplicate names are rejected
- **GIVEN** a registry that already contains a tool named `read`
- **WHEN** another tool named `read` is added
- **THEN** the registration fails immediately

### Requirement: Tool output truncation
`ToolOutput.Truncate` SHALL bound tool output to a default of 30,000 characters: output at or under the limit SHALL pass through unchanged; longer output SHALL keep the first half and last half of the budget with a marker stating the omitted character count (culture-invariant); cut points SHALL never split a UTF-16 surrogate pair; limits smaller than the marker SHALL degrade gracefully without throwing.

#### Scenario: Short output passes through
- **GIVEN** output within the limit
- **WHEN** it is truncated
- **THEN** the same string is returned unchanged

#### Scenario: Long output is cut in the middle with a marker
- **GIVEN** output longer than the limit
- **WHEN** it is truncated
- **THEN** the result contains the head, the marker with the omitted count and the tail, and the omitted count matches the removed characters

#### Scenario: Cut points never split surrogate pairs
- **GIVEN** output whose cut points fall inside an emoji
- **WHEN** it is truncated
- **THEN** the result contains no lone surrogate halves

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

### Requirement: C# diagnostics tool
The optional `cs_diagnostics` tool SHALL report compiler errors and warnings for files changed since the last check (default) or for the whole solution, without requiring a full `dotnet build`. It SHALL be marked read-only and SHALL load the C# backend lazily on first use — no Roslyn or MSBuild assembly SHALL be touched before the first call. Results SHALL carry file, 1-based line and column, severity, code and message, capped with a truncation flag; the tool text SHALL summarize counts and surface actionable statuses (`no SDK`, `restore required`, `no solution`, partial load) instead of raw exceptions. A backend failure SHALL never crash the run.

#### Scenario: An error introduced by an edit is reported
- **GIVEN** a loaded solution copy with clean diagnostics
- **WHEN** a source file is edited to contain a compile error and diagnostics run for changed files
- **THEN** the error is reported with its file and position, and after fixing it diagnostics are clean again

#### Scenario: No SDK is a clear message
- **GIVEN** a machine where no .NET SDK can be located
- **WHEN** `cs_diagnostics` runs
- **THEN** the result explains the missing SDK instead of throwing

#### Scenario: Missing restore is caught before compiling
- **GIVEN** a project whose `obj/project.assets.json` is absent
- **WHEN** diagnostics run
- **THEN** the result asks for `dotnet restore` with the project path instead of the diagnostics avalanche

#### Scenario: Partial loads are surfaced
- **GIVEN** a solution with unsupported projects or broken references
- **WHEN** it loads
- **THEN** the load result is partial, carries bounded failure details, and diagnostics still work for the loaded projects

#### Scenario: Assemblies load only on first call
- **GIVEN** a process that has constructed the tool but never executed it
- **WHEN** loaded assemblies are inspected
- **THEN** no `Microsoft.CodeAnalysis.*` or `Microsoft.Build.*` assembly is loaded yet
