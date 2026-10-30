## ADDED Requirements

### Requirement: Roslyn is the built-in C# tools provider

The host SHALL serve `Lunate.Roslyn` through one built-in extension with id `roslyn` and a manifest
declaring `builtin: true`, the five existing local tool names, `workspaceServices: [{ name: "roslyn",
maxLoadedWorkspaces: 2 }]`, and a default `toolActivation` rule of `workspace-root-has-file` with
patterns `*.sln`, `*.slnx` and `*.csproj`. Each tool call SHALL acquire the `roslyn` service for that
call's workspace root through `IExtensionContext.AcquireWorkspaceServiceAsync` and release its lease
when the call ends; the service SHALL retain the existing host `roslyn.maxLoadedWorkspaces` override,
which may lower but not raise the manifest cap.

The host's `ToolActivationEvaluator` SHALL be the sole C# activation gate for chat, child-workspace
runs and `lunate mcp roslyn`. The manifest rule SHALL replace the `HarnessFactory`-owned gate; a
second C#-specific scan or gate SHALL NOT be composed. The MCP server SHALL evaluate the same rule
through that evaluator for its selected root, as required by *Conditional tool availability*.

#### Scenario: The Roslyn built-in registers and leases its workspace service

- **GIVEN** the host composes its built-in extensions
- **WHEN** a C# tool is called for a workspace root
- **THEN** `/extensions` identifies one `roslyn` extension with scope `builtin`, its handler acquires
  the `roslyn` workspace service through the extension context for that root, and it releases the
  lease at the end of the call

#### Scenario: The manifest rule replaces the core gate

- **GIVEN** a root with a direct-child `.sln`, `.slnx` or `.csproj`
- **WHEN** chat, a child run or `lunate mcp roslyn` composes its tools
- **THEN** the shared host evaluator applies the built-in manifest rule and offers the C# tools; a
  root with none of those files offers none, without a second `HarnessFactory` gate

### Requirement: The Roslyn built-in preserves the recorded C# tool contract and budgets

Migration SHALL preserve exactly the model-facing tool names `cs_diagnostics`, `cs_find_symbol`,
`cs_find_references`, `cs_outline` and `cs_rename`. For the same committed fixtures and inputs, each
tool's `ToolResult.Output` and `IsError` SHALL match the recorded pre-migration baseline, and every
behavior already specified by the C# tool requirements in `agent-tools` SHALL remain true.

The migration SHALL record the baseline in `docs/eval/roslyn-built-in-migration-baseline.md` before
changing composition and compare the migrated build against it using the same fixture, runtime and
measurement method. The evidence SHALL include the startup floor and median, tool output and error
flags for all five tools, idle process-tree memory, and Roslyn process-tree peak memory after loading
the reference solution and after ten edit-to-diagnostics cycles. The startup floor SHALL remain
within the existing platform gate and shall not load Roslyn or MSBuild assemblies before the first
`cs_*` call. Idle memory SHALL remain within the existing ADR-0009 gate, and the measured Roslyn
peak SHALL remain within its existing reference-workspace gate of 750 MB; the built-in workspace
cap SHALL remain two. The evidence record SHALL identify the baseline and migrated commits,
runtime, OS/RID, fixture and measurement commands; deterministic result fixtures SHALL be committed
under `tests/fixtures/`.

#### Scenario: All five tools match the recorded baseline

- **GIVEN** the same fixture workspace and recorded input for each C# tool
- **WHEN** the pre-migration and migrated builds execute those calls
- **THEN** the declarations are exactly the five `cs_*` names and each `Output` and `IsError` value
  matches the committed baseline evidence

#### Scenario: Startup stays lazy and inside its floor gate

- **GIVEN** a published migrated binary that has not called a C# tool
- **WHEN** the existing startup-floor measurement and lazy-loading check run
- **THEN** the floor remains within the platform budget, no Roslyn or MSBuild assembly is loaded,
  and the measured floor and median are recorded beside the pre-migration baseline

#### Scenario: Roslyn memory stays within its recorded gate

- **GIVEN** the reference solution and the existing two-workspace cap
- **WHEN** peak process-tree memory is measured after load and after ten edit-to-diagnostics cycles
- **THEN** the Roslyn peak remains within 750 MB per reference workspace and both measurements are
  recorded against the pre-migration baseline

### Requirement: The built-in status and old sessions remain understandable

`/extensions` SHALL show Roslyn as a built-in, never as pending or trust-denied, and SHALL report its
five declared tools as active or with the existing activation rule and reason. It SHALL offer no
uninstall, unload or reload action for this host-owned built-in. A session written before migration
SHALL resume after migration, and a migrated session SHALL remain resumable after rolling back to the
pre-migration host, without a session-format change or renamed `cs_*` tool calls. A running process
SHALL keep its already-composed tool registry; a restart or resume SHALL compose the version's tools.

#### Scenario: `/extensions` reports the built-in's active state

- **GIVEN** a matching C# workspace and a non-matching workspace
- **WHEN** the user opens `/extensions` in each
- **THEN** both show `roslyn` with scope `builtin`, the matching workspace shows the five tools
  active, and the non-matching workspace reports them inactive with the manifest rule and reason;
  neither reports pending/trust-denied or offers built-in lifecycle actions

#### Scenario: A pre-migration session resumes and rolls back

- **GIVEN** a session file created before migration containing one or more `cs_*` calls
- **WHEN** it is resumed by the migrated build and, separately, by the pre-migration build after a
  rollback
- **THEN** its recorded names and results remain valid, the same five names are offered where
  activated, and no session conversion or format change is required
