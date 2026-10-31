## MODIFIED Requirements

### Requirement: Extension contract assembly

Extensions SHALL compile against `Lunate.Extensibility.Abstractions` and `Lunate.Contracts`, plus
`Microsoft.Extensions.AI.Abstractions`; they SHALL NOT reference `Lunate.Agent` or other Lunate
assemblies directly. The tool contract types (`ITool`, `ToolContext`, `ToolResult`, `ToolResultView`,
tool declarations, and tool annotations including `ToolAnnotations`, `ToolExposure` and `ToolRisk`) and
the complete `AgentEvent` hierarchy SHALL live in `Lunate.Contracts`; the old namespaces SHALL remain
reachable through type forwards in their former assemblies. `Lunate.Extensibility.Abstractions` SHALL
reference `Lunate.Contracts` and SHALL NOT reference `Lunate.Agent`. `Lunate.Contracts` SHALL depend
only on the BCL and `Microsoft.Extensions.AI.Abstractions`. The extension contract SHALL be semver'd
with a PublicAPI-tracked surface, and its entry point SHALL be
`IExtensionFactory.Create(IExtensionContext)` returning an `IExtension`. The context SHALL expose the
extension id, settings, secrets and a log sink. The current API version SHALL be a single constant.
`Lunate.Contracts` and `Lunate.Extensibility.Abstractions` SHALL be versioned in lockstep under
`ExtensionApi`.

#### Scenario: An extension compiles against the contract assemblies only

- **GIVEN** an extension project referencing `Lunate.Contracts`,
  `Lunate.Extensibility.Abstractions` and `Microsoft.Extensions.AI.Abstractions`
- **WHEN** it is built
- **THEN** it compiles without a direct reference to `Lunate.Agent` or another Lunate assembly

#### Scenario: Shared types live in Contracts

- **GIVEN** the public tool and event contracts
- **WHEN** their defining assemblies are inspected
- **THEN** `ITool`, tool context/results/declarations/annotations and every `AgentEvent` subtype are
  defined in `Lunate.Contracts`, while the event channel and sequence validator remain in
  `Lunate.Agent`

#### Scenario: The API version is queryable

- **GIVEN** the extension contract assemblies
- **WHEN** the current API version is read
- **THEN** it is a single semver constant that the range matcher validates against, and both
  assemblies have that API version

### Requirement: Extension loading

The host SHALL discover extensions in `~/.lunate/extensions/` (global) and `.lunate/extensions/`
(project), reading manifests only — assemblies SHALL load lazily on first use into a collectible
`AssemblyLoadContext` that shares `Lunate.Contracts`, `Lunate.Extensibility.Abstractions`,
`Lunate.Agent`, `Microsoft.Extensions.AI.Abstractions` and `System.Text.Json` with the default
context, and isolates every other dependency. Host-supplied built-in descriptors SHALL be folded in
before the two directory scans (see *Built-in extension provenance*), in the composition's
deterministic order. Unload SHALL be idempotent; a later load SHALL work; state SHALL not survive.
Duplicate ids across scopes SHALL be an error naming both paths, and a discovered id equal to a
built-in id SHALL be skipped naming both sources. Missing dependencies SHALL yield an actionable
error. The CLI SHALL compose the host when a session starts — the host's first use — for interactive,
print and ACP sessions; `--version`, `--help` and non-session commands SHALL NOT read the extension
store. `--no-extensions` SHALL compose only the built-in set, with no store scan, no third-party
extension, and no third-party extension MCP server, renderer or widget; configured `mcp.json`
servers are unaffected. A discovery or load failure SHALL be logged through the host log and SHALL
NOT abort the session.

#### Scenario: Manifests are read without loading assemblies

- **GIVEN** an installed extension
- **WHEN** discovery runs
- **THEN** its manifest is read and its assembly is not loaded

#### Scenario: A loaded extension unloads and reloads

- **GIVEN** a loaded extension
- **WHEN** it is unloaded and loaded again
- **THEN** both operations succeed and the second instance is fresh

#### Scenario: A missing dependency fails actionably

- **GIVEN** an extension whose entry assembly requires an assembly that is not present
- **WHEN** it is loaded
- **THEN** loading fails with an error naming the missing assembly and the extension

#### Scenario: A session composes the host at start

- **GIVEN** an extension installed in `~/.lunate/extensions/`
- **WHEN** an interactive, print or ACP session starts
- **THEN** the extension is discovered and loaded as the session starts, and its hook observes the
  session's tool calls

#### Scenario: Non-session commands never touch the store

- **GIVEN** any installed extension
- **WHEN** a non-session command (`lunate --version`, `lunate --help`, `lunate --discover`) runs
- **THEN** no manifest is read and no assembly is loaded

#### Scenario: One broken extension does not stop the session

- **GIVEN** an installed extension whose load fails
- **WHEN** a session starts
- **THEN** the failure is logged through the host log and the session runs without that extension

#### Scenario: --no-extensions starts without the host

- **GIVEN** an installed extension and a globally configured `mcp.json` server
- **WHEN** a session starts with `--no-extensions`
- **THEN** the store is not read, the installed extension's hook never runs, the built-in set is the
  only extension content composed and runs, and the configured MCP server still works

### Requirement: Host assemblies are the ones an extension loads

A built single-file extension SHALL load into its own collectible context, with every dependency
resolved from the exact asset its build manifest records for a locked package — never re-resolved —
and from the build directory otherwise. `Lunate.Contracts`, `Lunate.Extensibility.Abstractions`,
`Lunate.Agent`, `Microsoft.Extensions.AI.Abstractions` and `System.Text.Json` SHALL always resolve to
the host's copies, including when a locked package ships its own and when a packaged extension's
directory carries one: the copy SHALL be ignored, the load SHALL succeed, and exactly one line SHALL
name what was dropped and who shipped it. That line SHALL be logged and SHALL appear on the
extension's `/extensions` row (and as `ignoredCopy` in `--json`), because which copy is live is not
something a user can see for themselves. The authoring template and the authoring page SHALL show
references to `Lunate.Contracts` and `Lunate.Extensibility.Abstractions` as
`PrivateAssets="all"`, `ExcludeAssets="runtime"` and `Private="false"`, with one sentence saying
the host owns those assemblies.

#### Scenario: A copied Abstractions is ignored, not loaded twice

- **GIVEN** a locked package that ships `Lunate.Extensibility.Abstractions.dll`, and a second package
  that ships another copy
- **WHEN** the extension loads
- **THEN** the contract types in the loaded extension are the host's (the extension reports
  `typeof(IExtensionFactory).Assembly.Location` as the host's file, and its context holds no second
  Abstractions), the load succeeds, and exactly one line names both packages

#### Scenario: A packaged extension's own copy is ignored too

- **GIVEN** a packaged extension directory that contains a copy of a host-owned contract assembly
- **WHEN** it loads
- **THEN** the host's copy is used and the row and the log name the directory once

#### Scenario: A Contracts copy shares the host's type identity

- **GIVEN** a packaged extension directory or locked package that carries its own
  `Lunate.Contracts.dll`
- **WHEN** the extension loads
- **THEN** the copy is ignored, the extension resolves `ITool` and `AgentEvent` from the host's
  `Lunate.Contracts` assembly, and only one Contracts identity is loaded

#### Scenario: The template keeps both contracts out of the output

- **GIVEN** the packaged-extension template
- **WHEN** its project file is read
- **THEN** its `Lunate.Contracts` and `Lunate.Extensibility.Abstractions` references carry
  `PrivateAssets="all"`, `ExcludeAssets="runtime"` and `Private="false"`, and neither DLL is
  written to the extension's output

### Requirement: The API gate is the version the build saw

An extension's compatibility SHALL be decided by the API version its build saw — the `apiVersion` a
single-file build records in `build.json`, the `Lunate.Contracts` and
`Lunate.Extensibility.Abstractions` assembly references a packaged DLL carries, and the range a
manifest declares — never by the presence of a DLL and never at the first call. A version above the
host's `ExtensionApi.Current` SHALL refuse the load before any type of the extension is opened, with
`requires Lunate ≥ x.y` naming both the required version and the running one; a version at or below
it SHALL load, so a file compiled before an API bump keeps working. Discovery SHALL refuse a
packaged manifest whose declared range the host cannot meet using the same wording when the range's
floor is above the host. The `Lunate.Contracts` and `Lunate.Extensibility.Abstractions` assembly
versions SHALL both equal `ExtensionApi.Current`, because a refusal that names a version no Lunate
ships is worse than no refusal.

#### Scenario: At or below the host loads

- **GIVEN** a build recording `apiVersion` equal to the host's, and another recording an older one
- **WHEN** each loads
- **THEN** both load and neither reports `requires Lunate`

#### Scenario: Above the host is refused, naming both versions

- **GIVEN** a build recording `apiVersion` above the host's `ExtensionApi.Current`
- **WHEN** it loads
- **THEN** the load is refused with `requires Lunate ≥ <required> but this is Lunate <running>`, and
  no tool or command of the extension was registered

#### Scenario: A packaged DLL built against a newer contract is refused

- **GIVEN** a packaged extension whose assembly references either contract assembly at a version
  above the host's, while its manifest declares a compatible range
- **WHEN** it loads
- **THEN** the load is refused with `requires Lunate ≥ <that version>` before the factory is looked
  for, so a manifest cannot talk the host into opening code it cannot support
