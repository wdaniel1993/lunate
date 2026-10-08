## ADDED Requirements

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
