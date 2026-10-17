# agent-tools — delta for roslyn-refresh-rebaseline

## MODIFIED Requirements

### Requirement: C# diagnostics tool

The optional `cs_diagnostics` tool SHALL report compiler errors and warnings for files changed since the last check (default) or for the whole solution, without requiring a full `dotnet build`. In-session mutations SHALL be tracked through the file-change bus as exact marks — a tool edit SHALL be detected even when its content stamp is unchanged — while changes made outside the tool path SHALL be detected by content stamps; a same-stamp change made outside the tool path is seen only at the next reload. A change to a project, solution or build-configuration file (`.csproj`, `.sln`, `.slnx`, `Directory.Build.props`, `Directory.Build.targets`, `Directory.Packages.props`, `global.json`) SHALL mark the workspace for a reload on the next call. A file flagged by both the mark and the stamp scan SHALL be refreshed once (one compile, one changed-file count). A refresh SHALL NOT re-flag the refreshed file: the freshness baseline SHALL be re-taken after the refreshed text is applied, so a check with no further edits reports no changed files. A write racing a refresh SHALL NOT be overwritten by the refresh or swallowed by its re-baseline: only a read whose stamp still matches the disk SHALL be applied and re-baselined. It SHALL be marked read-only and SHALL load the C# backend lazily on first use — no Roslyn or MSBuild assembly SHALL be touched before the first call. Results SHALL carry file, 1-based line and column, severity, code and message, capped with a truncation flag; the tool text SHALL summarize counts and surface actionable statuses (`no SDK`, `restore required`, `no solution`, partial load) instead of raw exceptions. A backend failure SHALL never crash the run.

#### Scenario: An error introduced by an edit is reported

- **GIVEN** a loaded solution copy with clean diagnostics
- **WHEN** a source file is edited to contain a compile error and diagnostics run for changed files
- **THEN** the error is reported with its file and position, and after fixing it diagnostics are clean again

#### Scenario: A bus-delivered edit is reflected without a stamp change

- **GIVEN** a loaded solution copy with clean diagnostics
- **WHEN** a source file is rewritten with different content but an unchanged content stamp and the file-change bus delivers the change
- **THEN** diagnostics report the new state without reloading the workspace

#### Scenario: A project file change reloads the workspace

- **GIVEN** a loaded solution copy
- **WHEN** a project, solution or build-configuration file (`.csproj`, `.sln`, `.slnx`, `Directory.Build.props`, `Directory.Build.targets`, `Directory.Packages.props`, `global.json`) changes and the file-change bus delivers the change
- **THEN** the next diagnostics call reloads the workspace once before reporting

#### Scenario: An edit during a load is not lost

- **GIVEN** a workspace whose first load is in flight
- **WHEN** a tool edit arrives through the bus before the load completes
- **THEN** the load completes and the edit is reflected in the next diagnostics call

#### Scenario: A file flagged by both paths is refreshed once

- **GIVEN** a loaded solution copy where a file is flagged by both the bus mark and the stamp scan
- **WHEN** diagnostics run for changed files
- **THEN** the file is reported once (one changed-file count)

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

#### Scenario: A refreshed file stays clean on the next check

- **GIVEN** a loaded solution copy with clean diagnostics
- **WHEN** a source file is edited, diagnostics run for changed files, and diagnostics run again with no further edits
- **THEN** the first run reports the file as changed and the second reports no changed files

#### Scenario: A write racing a refresh is neither overwritten nor swallowed

- **GIVEN** a loaded solution copy
- **WHEN** a source file is written while a refresh is reading it, and diagnostics run again
- **THEN** the racing write remains on disk and the next run reports the file as changed
