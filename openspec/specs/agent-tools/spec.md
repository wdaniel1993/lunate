# agent-tools Specification

## Purpose
The tool surface of the agent: what a tool is (our own contract with risk levels, working directory and events), how it is declared to the model without Microsoft.Extensions.AI ever invoking it, how tools are registered and found by name, and how tool output is bounded before it enters the conversation.

## Requirements

### Requirement: Tool contract

`Lunate.Agent` SHALL define tools as its own type: `ITool` with a name, a description, a hand-written JSON `ParametersSchema`, a `ToolRisk` (`ReadOnly`, `Write`, `Execute`) and `ExecuteAsync(JsonElement args, ToolContext ctx, CancellationToken ct)`; results as `ToolResult` (output, error flag, UI-only `Details`); and `ToolContext` carrying the working directory, an event sink that accepts only extension events, and the per-run subagent seam (`Subagents`, see the agent-loop specification — it starts child runs with an optional allow-list of the run's own tools and only-tightening limits) stamped by the harness alongside the run id and file-change sink. Schemas SHALL be authored by hand, never built by reflection. Error results SHALL reach the model through their instructing output text; a provider-level error flag is not sent (revisit when real tools and a live fixture exist). `ToolContext` SHALL also carry the active `ModelInfo` when the run has one, so a tool can decide whether the model supports a capability; it SHALL be null outside a run. `ToolResult` SHALL optionally carry `Images`, a list of `ImageAttachment` values (media type, width, height and bytes); `Output` SHALL remain the only model-facing text and SHALL contain the `[image <w>x<h>]` placeholder for an image result, never image bytes.

#### Scenario: Contract is implementable without reflection
- **GIVEN** a tool implemented in a test with a hand-written schema
- **WHEN** it is exercised through the contract types
- **THEN** no reflection is involved and the result carries output, error flag and optional UI-only details

#### Scenario: Details never travel to the model
- **GIVEN** a tool result with `Details` set
- **WHEN** the loop appends the tool result to the conversation
- **THEN** only the output reaches the model — instructing text for error results — while details stay UI-only and the error flag feeds events and telemetry

#### Scenario: The subagent seam is stamped per run
- **GIVEN** a tool executed by the loop
- **WHEN** it reads its context
- **THEN** `Subagents` exposes the run's runner (its `Depth` being the run's spawn depth, shared by the run's parallel calls), and a tool executed outside the loop sees `Subagents` as null

#### Scenario: The active model is visible to a tool

- **GIVEN** a run on a catalog model with vision
- **WHEN** a tool reads its context
- **THEN** `Model` names that model and reports `SupportsVision` as true; outside a run it is null

#### Scenario: An image result carries bytes beside the placeholder

- **GIVEN** a tool returning an image attachment
- **WHEN** its result is inspected
- **THEN** `Output` is `[image <w>x<h>]`, `Images` holds the bytes and media type, and `Details` is unchanged

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
`ToolOutput.Truncate` SHALL bound tool output to a default of 30,000 characters: output at or under the limit SHALL pass through unchanged; longer output SHALL keep the first half and last half of the budget with a marker stating the omitted character count (culture-invariant) and, when a spill file was written, its absolute path; cut points SHALL never split a UTF-16 surrogate pair; limits smaller than the marker SHALL degrade gracefully without throwing.

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

#### Scenario: The full output survives in a spill file

- **GIVEN** a session run and a tool result larger than the configured limit
- **WHEN** the result is truncated
- **THEN** the full output is written to a file in the session's own directory, the marker names that absolute path, and `read` on it returns the full content

#### Scenario: No spill without a session

- **GIVEN** a `--no-session` run and a tool result larger than the limit
- **WHEN** the result is truncated
- **THEN** no file is written and the marker keeps the count-only form

#### Scenario: A failed spill never fails the tool

- **GIVEN** a session directory that cannot be written
- **WHEN** a result over the limit is truncated
- **THEN** the tool result is still delivered with the count-only marker and the failure is reported as a notice, not a tool error

### Requirement: Tool exposure and registry declarations

`ITool` SHALL gain an `Exposure` (`Direct`, `ModelOnly`, `Programmatic`, `Deferred`, `Hidden`, default `Direct`) as a default interface member. `ToolRegistry.Declarations` SHALL expose only `Direct` and `ModelOnly` tools to the model; `Deferred` tools are never declared and are discoverable through the run's tool stubs. Nested/programmatic execution SHALL allow `Direct`, `Programmatic` and `Deferred` tools and refuse `ModelOnly` and `Hidden` with an instructing error.

#### Scenario: Hidden and programmatic tools are not declared
- **GIVEN** registered tools with exposures `Direct`, `ModelOnly`, `Programmatic` and `Hidden`
- **WHEN** declarations are built
- **THEN** only `Direct` and `ModelOnly` appear

#### Scenario: The programmatic gate refuses model-only tools
- **GIVEN** a tool with exposure `ModelOnly`
- **WHEN** it is called through `ToolContext.ExecuteToolAsync`
- **THEN** the call is refused with an error naming the exposure

#### Scenario: Deferred tools are callable but never declared
- **GIVEN** a tool with exposure `Deferred`
- **WHEN** declarations are built and the tool is called through `ToolContext.ExecuteToolAsync`
- **THEN** it appears in neither the declarations nor the model's tool list, but the nested call executes and its stub appears in the run's tool stubs

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

The optional `cs_diagnostics` tool SHALL report compiler errors and warnings for files changed since the last check (default) or for the whole solution, without requiring a full `dotnet build`. In-session mutations SHALL be tracked through the file-change bus as exact marks — a tool edit SHALL be detected even when its content stamp is unchanged — while changes made outside the tool path SHALL be detected by content stamps; after each bash call the workspace's git status SHALL additionally be diffed against the previous scan of the same run and every path that really changed SHALL be published as an exact mark — a same-stamp bash edit to an already-dirty file included — and an already-dirty file SHALL NOT be re-published without a real change (a revert SHALL be published, a commit SHALL NOT); a same-stamp change made by any other out-of-band writer is seen only at the next reload. A change to a project, solution or build-configuration file (`.csproj`, `.sln`, `.slnx`, `Directory.Build.props`, `Directory.Build.targets`, `Directory.Packages.props`, `global.json`) SHALL mark the workspace for a reload on the next call. A file flagged by both the mark and the stamp scan SHALL be refreshed once (one compile, one changed-file count). A refresh SHALL NOT re-flag the refreshed file: the freshness baseline SHALL be re-taken after the refreshed text is applied, so a check with no further edits reports no changed files. A write racing a refresh SHALL NOT be overwritten by the refresh or swallowed by its re-baseline: only a read whose stamp still matches the disk SHALL be applied, and the baseline SHALL be re-taken only when the disk still holds the applied text, recorded as that verified state itself (never a stamp read after the verification). It SHALL be marked read-only and SHALL load the C# backend lazily on first use — no Roslyn or MSBuild assembly SHALL be touched before the first call. Results SHALL carry file, 1-based line and column, severity, code and message, capped with a truncation flag; the tool text SHALL summarize counts and surface actionable statuses (`no SDK`, `restore required`, `no solution`, partial load) instead of raw exceptions. A backend failure SHALL never crash the run.

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

#### Scenario: A same-stamp bash edit is marked and refreshes without a reload

- **GIVEN** a loaded solution copy with clean diagnostics and a bash call that rewrote a source file with different content but an unchanged content stamp
- **WHEN** the next diagnostics call runs for changed files
- **THEN** the new state is reported without reloading the workspace

#### Scenario: An already-dirty file is not re-published by an unrelated bash call

- **GIVEN** a workspace with a file that was already dirty when the run's first bash call baselined
- **WHEN** a later bash call changes a different file
- **THEN** only the different file is published; the already-dirty file is not re-flagged

#### Scenario: A cancelled bash call is still scanned

- **GIVEN** a bash call cancelled mid-command that had already written a file
- **WHEN** the cancellation propagates
- **THEN** the change is still published before the cancellation surfaces

### Requirement: C# symbol lookup tool
The optional `cs_find_symbol` tool SHALL find definitions for a type, member or namespace name in the loaded solution and return their signature and source location (file, 1-based line and column), without grepping. It SHALL be read-only, SHALL load the C# backend lazily on first use, and SHALL reuse the diagnostics tool's load statuses (`no SDK`, `restore required`, `no solution`, partial load) as actionable messages. Matches SHALL be deterministic (stable ordering), bounded with a truncation flag, and SHALL mark metadata-only symbols (no source location) distinctly. An unknown name SHALL produce an empty result with a short hint, never an error.

#### Scenario: A type is found with its signature and location
- **GIVEN** a loaded fixture solution containing a known type
- **WHEN** `cs_find_symbol` searches for its name
- **THEN** the match carries the file, 1-based line and column, and the pinned signature format

#### Scenario: Overloads and partial types return every definition
- **GIVEN** a fixture with an overloaded method and a partial type
- **WHEN** the method name or partial type name is searched
- **THEN** each definition is listed as a separate match in stable order

#### Scenario: Unknown names are not errors
- **GIVEN** a loaded solution
- **WHEN** a name that does not exist is searched
- **THEN** the result is empty with a hint, and the tool reports success

#### Scenario: Load statuses carry through
- **GIVEN** no solution or a missing SDK
- **WHEN** `cs_find_symbol` runs
- **THEN** the result explains the condition the same way `cs_diagnostics` does

### Requirement: C# references tool
The optional `cs_find_references` tool SHALL list all source references (usage locations; declaration sites excluded and reported separately) for an exactly resolved symbol — a simple name only when unique, a dotted container path for disambiguation — with file, 1-based line and column, bounded with a truncation flag. Ambiguous names SHALL return candidates with a disambiguation hint, never a guess; metadata-only symbols SHALL return an explanatory message rather than an error; the tool SHALL be read-only.

#### Scenario: References across projects are found
- **GIVEN** a loaded multi-project fixture and a symbol used in both projects
- **WHEN** `cs_find_references` searches for it
- **THEN** usage locations from both projects are listed in stable order, the declaration is reported once separately, and a second run is identical

#### Scenario: Ambiguity is a hint, not a guess
- **GIVEN** two types with a member of the same simple name
- **WHEN** the simple name is searched
- **THEN** the result lists candidates with a hint to use a dotted path, and no references

### Requirement: C# outline tool
The optional `cs_outline` tool SHALL list one file's types and member signatures without bodies — kind, container path, name, declaration signature and 1-based line, in source order, bounded with a truncation flag — and SHALL NOT require a loaded solution (syntax-level; works when no solution or SDK is available). A missing or unreadable file SHALL return an actionable message, never an error.

#### Scenario: A big file gets a cheap outline
- **GIVEN** a source file with nested types and mixed members
- **WHEN** `cs_outline` runs for it
- **THEN** signatures carry no body text, nesting is visible via container paths, and results are in source order

#### Scenario: Outline works without a solution
- **GIVEN** a backend with no loadable solution
- **WHEN** a file under the root is outlined
- **THEN** the outline is produced (no solution required) while statuses elsewhere stay unchanged

### Requirement: C# rename tool
The optional `cs_rename` tool SHALL compute a solution-wide rename plan for a resolved symbol — per-file, bounded occurrence lists with before/after text and totals, deterministic order — and SHALL NOT modify any file or the workspace: the plan is applied only through the existing edit/approval path. `newName` SHALL be validated as a legal C# identifier; ambiguous names SHALL return candidates; metadata targets SHALL return a message; the tool SHALL be read-only and its text SHALL state that nothing was changed.

#### Scenario: Rename is planned, disk untouched
- **GIVEN** a loaded fixture with a symbol used in two projects
- **WHEN** `cs_rename` plans a rename
- **THEN** the plan lists both files with expected occurrences, and the fixture tree is byte-identical afterwards

#### Scenario: Invalid new names are rejected with guidance
- **GIVEN** a legal target symbol
- **WHEN** `newName` is not a legal C# identifier
- **THEN** the result explains why with no plan produced

### Requirement: Bash tool
The `bash` tool SHALL run a single non-interactive command through the platform's resolved shell, in the workspace root, with stdout and stderr captured separately, output decoded as UTF-8, both streams bounded with an explicit cap marker, and the exit code surfaced in the result (culture-invariant). The tool SHALL be marked `Execute` risk so the approval policy applies, its description SHALL name the resolved shell and dialect, and a missing shell SHALL produce a clear error result rather than a partial run. On timeout (default 120 s) or cancellation the process tree SHALL be terminated, children before parents (the mechanism is platform-specific: `ps`-walk + SIGKILL on POSIX, job-object tree kill on Windows); cancellation SHALL rethrow `OperationCanceledException`; `IsError` SHALL be true for non-zero exits, timeouts and spawn failures.

#### Scenario: Shell resolution follows the platform table
- **GIVEN** a Windows machine with Git Bash installed
- **WHEN** the shell is resolved
- **THEN** Git Bash wins with bash-dialect invocation, `C:\Windows\System32\bash.exe` is never selected, and the order falls through pwsh 7, Windows PowerShell, cmd only when earlier shells are absent

#### Scenario: A timeout kills the whole tree
- **GIVEN** a command that spawns a long-lived child
- **WHEN** the timeout expires
- **THEN** the result reports the timeout and neither the command nor its child remains alive

#### Scenario: Cancellation kills the tree and propagates
- **GIVEN** a running command
- **WHEN** the call's token is cancelled
- **THEN** the tree is killed and an `OperationCanceledException` is raised

#### Scenario: UTF-8 and exit codes round-trip
- **GIVEN** a command emitting non-ASCII text and exiting non-zero
- **WHEN** it runs
- **THEN** the output carries the text intact and the result shows the exit code with `IsError` true

#### Scenario: Output is bounded
- **GIVEN** a command producing more than the capture cap
- **WHEN** it runs
- **THEN** the captured output ends with the cap marker and memory stays bounded

### Requirement: Tool stubs

`Lunate.Agent` SHALL provide typed stub generation from a tool's JSON schemas: `ToolStubs.Describe(ITool)` SHALL derive a `ToolStub` — the model-facing name, description, typed parameters (name, type, required flag, description, raw default text, enum values) and whether the tool declares an `OutputSchema` — from the tool's `ParametersSchema`. Each parameter's JSON Schema `type` SHALL map to a documented type name (`string`, `number`, `integer`, `boolean`, `array`, `object`, `enum`, `any`); `required` SHALL come from the schema's `required` array; constructs the mapping does not recognise SHALL degrade to `any` rather than fail, and an absent or non-object schema SHALL yield zero parameters. `AgentHarness` SHALL stamp every run-built `ToolContext` with `ToolStubs`: the stubs of the run's programmatically callable tools (`Direct`, `Programmatic` and `Deferred` exposure) in registration order — never `ModelOnly` or `Hidden` — and the property SHALL be null outside a run, like the other per-run seams.

#### Scenario: Stubs mirror the declared schema

- **GIVEN** a tool whose parameter schema has required and optional properties, an enum and a default, plus one unrecognised construct
- **WHEN** its stub is described
- **THEN** parameters carry the mapped types, required flags, enum values, descriptions and defaults in declaration order, and the unrecognised construct degrades to `any`

#### Scenario: The stamped list is the callable set

- **GIVEN** a run whose registry holds tools of every exposure
- **WHEN** a tool reads `ToolContext.ToolStubs`
- **THEN** it contains the `Direct`, `Programmatic` and `Deferred` tools in registration order, and no `ModelOnly` or `Hidden` tool

#### Scenario: No run, no stubs

- **GIVEN** a `ToolContext` built outside a run
- **WHEN** `ToolStubs` is read
- **THEN** it is null

### Requirement: Optional search tools

When configured, three read-only tools SHALL be registered beside the core set: `grep` (regex search with `path`, `glob`, `case_sensitive` and `max_results` of 100 default / 500 cap, using the ripgrep binary when present and an internal walker otherwise, both honouring `.gitignore` and skipping binary files), `find` (glob matching with `path`, `type` of `file` or `dir`, 100/500 bounds, case-insensitive on Windows and macOS, case-sensitive on Linux) and `ls` (one directory listing with `path` and `depth` of 1 default / 3 max, directories first then files, `.git` always skipped). All three SHALL refuse a path outside the workspace exactly as `read` does, SHALL require no approval, SHALL follow no directory symlink and SHALL route their output through the existing truncation path. Their results SHALL name what was searched and SHALL end with a `[N more …]` line when a cap cut the result. When the configured list is empty — the default — none SHALL be registered, and the tool set SHALL be exactly the core set.

#### Scenario: Nothing extra by default

- **GIVEN** a settings file without `tools.extra`
- **WHEN** the harness is built
- **THEN** only the core tools are registered and no `grep`, `find` or `ls` exists

#### Scenario: A configured list registers exactly its tools

- **GIVEN** `tools.extra` set to `["grep", "ls"]`
- **WHEN** the harness is built
- **THEN** `grep` and `ls` are registered, `find` is not, and a `bash` name in the list is rejected with the accepted values

#### Scenario: grep honours gitignore and the cap

- **GIVEN** a workspace whose `.gitignore` excludes `bin/` and one file with 600 matching lines
- **WHEN** `grep` runs with a 100 default
- **THEN** nothing from `bin/` appears, exactly 100 matches are returned and the result ends with `[500 more matches]`

#### Scenario: A path outside the workspace is refused

- **GIVEN** a `grep` call with `path: "../.."`
- **WHEN** the workspace checks it
- **THEN** the call is refused naming the path, the resolved target and the allowed roots, and no file is read

#### Scenario: ls skips .git at every depth

- **GIVEN** a repository root and `depth: 3`
- **WHEN** `ls` renders the tree
- **THEN** no `.git` entry appears and directories precede files at each level

### Requirement: Writes preserve the user's file

The write and edit tools SHALL preserve the file they change: an existing file keeps its dominant line ending and its byte-order mark, and a file with mixed endings is unified to the dominant one. A new file SHALL follow `.editorconfig` for its directory, then `.gitattributes`, else the platform default. Neither tool SHALL rewrite an untouched part of a file to a different line ending.

#### Scenario: A CRLF file stays CRLF
- **GIVEN** a file whose lines end with CRLF
- **WHEN** the write tool replaces its content
- **THEN** every line still ends with CRLF

#### Scenario: A byte-order mark survives a rewrite
- **GIVEN** a file that begins with a UTF-8 byte-order mark
- **WHEN** the edit tool changes one line
- **THEN** the mark is still there and the file's other bytes are unchanged

#### Scenario: A new file follows the project's convention
- **GIVEN** a directory whose `.editorconfig` sets `end_of_line = lf` on a machine whose default is CRLF
- **WHEN** the write tool creates a file there
- **THEN** the file uses LF

#### Scenario: A mixed file is unified, not multiplied
- **GIVEN** a file with both CRLF and LF lines, mostly CRLF
- **WHEN** the edit tool changes one line
- **THEN** the result uses CRLF throughout and the change is reported as the edit's own
