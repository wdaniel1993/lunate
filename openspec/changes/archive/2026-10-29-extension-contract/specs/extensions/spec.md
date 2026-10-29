# Extension deltas — the four gaps before stable

## ADDED Requirements

### Requirement: Built-in extension provenance

A normal manifest SHALL omit `builtin` (default `false`); a host-supplied built-in manifest SHALL
declare `builtin: true`. `builtin: true` SHALL mean the extension ships with the host: its assembly
is already in the default `AssemblyLoadContext`, its lifecycle and version are the host's, and it is
never installed, content-hash trusted, upgraded or uninstalled independently. A built-in SHALL be
supplied by the host composition (manifest plus an `IExtensionFactory`) and SHALL load through the
same `IExtensionContext`, registries, manifest cross-checks, background-services lifecycle and
teardown as a discovered extension. A host-supplied manifest without `builtin: true`, and a manifest
found by directory discovery that declares `builtin: true`, SHALL be refused with a message naming
the file and the reason. A discovered extension whose id equals a built-in id SHALL be skipped with one
discovery error naming both sources; a built-in SHALL never be shadowed. Built-ins SHALL load before
global and project descriptors, SHALL run in the default load context (no collectible ALC, no second
copy of a host assembly), and SHALL use the process-wide MSBuild bootstrap exactly once. A built-in
SHALL NOT be reloadable or unloadable in this version; session end stops its background services.
`--no-extensions` SHALL still compose the built-in set and its contributions, while skipping
discovery, trust, third-party loads, and third-party extension MCP servers, renderers and widgets,
so the model-facing behaviour the host ships stays available.

#### Scenario: A built-in loads through the shared activation path

- **GIVEN** a host-supplied built-in manifest and factory
- **WHEN** the session composes extensions
- **THEN** its factory runs against a normal extension context, its declared contributions enter the
  same registries, and its unload/teardown path is the one the loader uses for a discovered
  extension

#### Scenario: A directory cannot claim built-in provenance

- **GIVEN** a manifest under `~/.lunate/extensions/` or `.lunate/extensions/` with `builtin: true`
- **WHEN** discovery reads it
- **THEN** it is refused with a discovery error naming the path, and no built-in path is taken

#### Scenario: A host-supplied manifest must say it is built-in

- **GIVEN** a manifest supplied through the host's built-in list without `builtin: true`
- **WHEN** the composition loads built-ins
- **THEN** the composition refuses it with a message naming the manifest and the field

#### Scenario: A discovered extension cannot shadow a built-in

- **GIVEN** a built-in with id `roslyn` and a discovered extension with the same id
- **WHEN** discovery runs
- **THEN** the built-in is listed, the discovered descriptor is skipped, and one discovery error
  names both sources

#### Scenario: Built-ins run in the default context

- **GIVEN** a built-in whose assembly is already loaded by the host
- **WHEN** it loads
- **THEN** no collectible `AssemblyLoadContext` is created, no second Roslyn/MSBuild copy is loaded,
  and the process-wide MSBuild bootstrap is not registered twice

#### Scenario: --no-extensions keeps host built-ins

- **GIVEN** a session started with `--no-extensions`
- **WHEN** the tool set is composed
- **THEN** the built-in set is present and its contributions behave as without the flag, while no
  store is scanned and no third-party extension, extension MCP server, renderer or widget loads

### Requirement: Per-workspace extension services

`IExtensionContext` SHALL expose a registration-only workspace service facility. An extension SHALL
register a named service with a factory and declare it in the manifest `workspaceServices` list
(`name`, optional positive `maxLoadedWorkspaces`, default 2). A registered-but-undeclared service
SHALL refuse the load; a declared-but-unregistered service SHALL be logged and dropped. The
extension SHALL acquire the service for a canonical `WorktreeRoot` and receive a lease; the lease
SHALL pin the entry for its lifetime, and disposing it SHALL release the pin. Values SHALL be created
lazily on first acquisition per root, outside the registry lock, and disposed on eviction or
shutdown. The host SHALL cap loaded values per (extension, service) at the declared cap, evict the
least-recently-used unpinned entry, and SHALL NOT evict a pinned entry; when every loaded entry is
pinned, a new root's acquisition SHALL wait, cancellation-aware, rather than dispose under a live
call. On workspace switch the old root's entry SHALL remain loaded until it is the LRU candidate;
the same registry and cap SHALL apply to the session root and every child-run root. On shutdown the
registry SHALL release waiters, dispose every entry (pinned included), and fail later acquisitions
with `ObjectDisposedException`. Eviction SHALL dispose rebuildable resources only and SHALL NOT
delete durable data, sweep a shared store, or reset a session directory.

#### Scenario: A service is created lazily per workspace

- **GIVEN** an extension with a declared workspace service
- **WHEN** two runs in different worktrees acquire it for the first time
- **THEN** the factory runs once per root, each acquisition names its own root, and no value is
  created before the first acquisition

#### Scenario: A pinned entry is not evicted

- **GIVEN** the cap is held by a pinned entry
- **WHEN** another root acquires the service
- **THEN** the pinned entry stays loaded, the acquisition waits for a slot, and it proceeds when the
  lease is disposed or the caller cancels

#### Scenario: The LRU entry is evicted and disposed

- **GIVEN** the cap is held and at least one loaded entry is unpinned
- **WHEN** a new root acquires the service
- **THEN** the least-recently-used unpinned entry is removed and its value is disposed outside the
  registry lock, and the new root's value is created

#### Scenario: A workspace switch does not unload the old root

- **GIVEN** a loaded service for one worktree
- **WHEN** a child run in another worktree acquires the same service
- **THEN** the first entry stays loaded until it becomes the LRU candidate, and the shared cap
  applies across both roots

#### Scenario: Shutdown disposes every entry

- **GIVEN** a registry with a pinned and an unpinned entry
- **WHEN** the session shuts down
- **THEN** waiters are released, both entries are disposed, and a later acquisition fails with
  `ObjectDisposedException`

#### Scenario: Eviction never deletes durable data

- **GIVEN** a workspace service whose state includes files it did not create or a shared session
  store
- **WHEN** its entry is evicted
- **THEN** the value is disposed and no sweep, reference-count cleanup or directory reset runs

### Requirement: Conditional tool availability

A manifest SHALL accept an optional `toolActivation` object. A key SHALL be `*` (the default rule)
or a name declared in `tools`; any other key SHALL be a manifest error. A rule SHALL be a JSON object
with a `when` from the closed set `always`, `workspace-root-has-file` (with a non-empty `patterns`
array of direct-child file globs, no path separator) and `workspace-is-git-repository` (no extra
fields); an unknown condition or field SHALL be a manifest error. A declared tool with no applicable
rule SHALL always be available. The host SHALL evaluate the rule when it composes each run's tool
registry, against that run's workspace, and SHALL omit an inactive tool from the registry rather
than exposing it to the model, nested execution or programmatic stubs. The host SHALL evaluate only
at composition: a condition that changes while a run is in progress SHALL take effect at the next
composition (a child run, a reload that recomposes, or a new session), not by mutating the running
registry. An unreadable workspace SHALL evaluate a file condition as false and SHALL NOT fail the
session. `/extensions` SHALL report an inactive declared tool with the rule and the reason, and the
registered/declared counts SHALL be unchanged.

#### Scenario: The C# rule matches a solution at the root

- **GIVEN** a built-in manifest whose default rule is `workspace-root-has-file` with `*.sln`,
  `*.slnx` and `*.csproj`
- **WHEN** a run composes in a root containing one of those files
- **THEN** the tools are offered, and in a root with none of them they are absent from the
  declarations, nested calls and programmatic stubs

#### Scenario: The rule is evaluated by the host, not the extension

- **GIVEN** an extension with an activation rule and no code that scans the workspace
- **WHEN** a run composes in a matching and a non-matching root
- **THEN** the host decides both outcomes from the manifest rule and no extension code runs during
  prompt or tool-declaration building

#### Scenario: A mid-session change waits for the next composition

- **GIVEN** a running registry composed in a non-matching root
- **WHEN** a matching file appears at the root
- **THEN** the running registry is unchanged, and the next child run or new session in that root
  offers the tools

#### Scenario: Malformed rules are refused

- **GIVEN** a manifest with an unknown `when`, a path separator in a pattern, or a rule for an
  undeclared tool
- **WHEN** it is parsed
- **THEN** the error names the manifest, the tool key and the field, and the extension does not load

#### Scenario: The MCP server evaluates the rule through the same evaluator

- **GIVEN** `lunate mcp roslyn` running against a root, and the chat path's activation evaluator
- **WHEN** the server composes its tool list
- **THEN** it asks that evaluator rather than a copy of the rule, and a client's declared MCP `roots`
  are the roots evaluated; a client that declares no roots is answered for the server's working
  directory

#### Scenario: A root with no solution lists no cs_* tool

- **GIVEN** a root with no `.sln`, `.slnx` or `.csproj`
- **WHEN** `tools/list` is answered
- **THEN** it contains no `cs_*` tool, stderr carries exactly one line naming the rule and the
  patterns it looked for, and stdout carries nothing but protocol messages

#### Scenario: A new root announces tools/list_changed

- **GIVEN** a client that declared `listChanged` support, connected to a root that matched no rule
- **WHEN** the root gains a `.csproj` and the client sends `notifications/roots/list_changed`
- **THEN** the server re-evaluates, sends `notifications/tools/list_changed`, and a following
  `tools/list` contains the `cs_*` tools

#### Scenario: A client without listChanged keeps its snapshot

- **GIVEN** a client that did not declare `listChanged` support
- **WHEN** its roots change
- **THEN** the server sends no `tools/list_changed`, and the list the client received at
  `initialize` remains what it is served until it reconnects

#### Scenario: A solution removed without a roots event keeps the fix-it error

- **GIVEN** a listed `cs_*` tool whose root no longer holds the `.sln`/`.slnx` and no roots event was
  sent
- **WHEN** the tool is called
- **THEN** the call returns a tool error naming the missing file, and the tool stays listed because
  the composition it was built from has gone stale

### Requirement: Structured tool results

`ToolResultView` SHALL carry `StructuredContent` — a JSON object, JSON-serializable per ADR-0017
principle 3 — together with a `ContentKind` naming its schema (for example `lunate.diagnostics/v1`,
`lunate.diff/v1`, `lunate.command-result/v1`). A tool declaration SHALL be able to state an optional
output schema for its structured payload. The field names and shape SHALL mirror MCP's
`structuredContent` and `outputSchema`, so the MCP adapter maps structured results in both
directions. Built-in tools that already produce typed details SHALL project them into
`StructuredContent`; the internal `Details` handle SHALL NOT be published. The model SHALL continue
to receive `ResultText` exactly as before, and `StructuredContent` SHALL be the channel for
renderers, export, `--json` and MCP. The extension renderer surface SHALL receive
`StructuredContent` from the first stable release even if the renderer API itself stays minimal.

#### Scenario: A diagnostics result carries a typed payload

- **GIVEN** a `cs_diagnostics` call that returns two compiler errors
- **WHEN** the result is projected
- **THEN** `StructuredContent` holds the diagnostics and `ContentKind` is `lunate.diagnostics/v1`, and
  no consumer needs to parse `ResultText` to count or locate them

#### Scenario: An extension renderer renders from StructuredContent

- **GIVEN** an extension renderer for `lunate.diagnostics/v1`
- **WHEN** it is handed a `cs_diagnostics` result view
- **THEN** it dispatches on `ContentKind` and renders from `StructuredContent` without reading
  `ResultText`, and a result whose kind it does not know is rendered as text with a note rather than
  misrendered

#### Scenario: The model's prompt is unchanged

- **GIVEN** a tool result that carries `StructuredContent`
- **WHEN** the loop builds the next model request
- **THEN** the model receives the same `ResultText` it received before this contract, and the
  structured payload appears nowhere in the prompt

#### Scenario: MCP structured results map in both directions

- **GIVEN** a configured MCP server that returns `structuredContent`, and `lunate mcp roslyn` serving
  `cs_diagnostics`
- **WHEN** each result crosses the adapter
- **THEN** the former arrives as `StructuredContent` with a kind derived from its schema, and the
  latter is emitted as MCP `structuredContent` alongside its text content

### Requirement: Stable tool aliases

A manifest SHALL accept an optional `toolAliases` object mapping a declared tool's local name to a
model-facing alias. The canonical model-facing name SHALL remain `<extension-id>__<local-name>` for
identity, diagnostics and discovery; an alias SHALL be the name the model sees and SHALL be used for
declarations, nested calls, renderers and result views. An alias SHALL match `[A-Za-z0-9_-]{1,64}`,
SHALL NOT contain `__`, and SHALL NOT be a core tool name. Aliases SHALL be globally unique across
built-in, extension and canonical names; a collision SHALL refuse the later load with a message
naming both owners, except a collision with an MCP tool's model-facing name, which SHALL keep the
existing first-composition-wins rule (extension tools first, the MCP tool skipped with one
single-line diagnostic). An alias for an undeclared or unregistered tool SHALL be a manifest error.
A tool with no alias SHALL keep the canonical name. An alias SHALL be part of the extension's
published model-facing surface; changing or removing one is a breaking change that the host does not
rename for the extension.

#### Scenario: The flagship keeps cs_* without a rename

- **GIVEN** a built-in manifest declaring local names `cs_diagnostics`, `cs_find_symbol`,
  `cs_find_references`, `cs_outline` and `cs_rename` with aliases of the same values
- **WHEN** the session composes tools
- **THEN** the model sees exactly those five names, unchanged from today

#### Scenario: An extension can publish an alias

- **GIVEN** an extension whose local tool `diagnostics` declares the alias `cs_diagnostics`
- **WHEN** the tool is offered
- **THEN** the declaration carries `cs_diagnostics` while the extension keeps its own id and local
  name for diagnostics and grouping

#### Scenario: A collision refuses the later load

- **GIVEN** two extensions declaring the same alias, or an alias equal to a core tool name or
  another extension's canonical name
- **WHEN** the later extension loads
- **THEN** the load is refused with one message naming both owners

#### Scenario: An MCP collision keeps the existing rule

- **GIVEN** an extension alias and an MCP tool with the same model-facing name
- **WHEN** the session composes tools
- **THEN** the extension tool is registered, the MCP tool is skipped with one single-line
  diagnostic naming both sources, and the session continues

#### Scenario: An unknown alias target is refused

- **GIVEN** a `toolAliases` entry for a name not declared in `tools`
- **WHEN** the manifest is parsed
- **THEN** the error names the manifest, the tool and the field

## MODIFIED Requirements

### Requirement: Extension loading

The host SHALL discover extensions in `~/.lunate/extensions/` (global) and `.lunate/extensions/`
(project), reading manifests only — assemblies SHALL load lazily on first use into a collectible
`AssemblyLoadContext` that shares the contract assemblies — `Lunate.Extensibility.Abstractions` and
the `Lunate.Agent` types it references — and `Microsoft.Extensions.AI.Abstractions` and
`System.Text.Json` with the default context, and isolates every other dependency. Host-supplied
built-in descriptors SHALL be folded in before the two directory scans (see *Built-in extension
provenance*), in the composition's deterministic order. Unload SHALL be idempotent; a later load
SHALL work; state SHALL not survive. Duplicate ids across scopes SHALL be an error naming both
paths, and a discovered id equal to a built-in id SHALL be skipped naming both sources. Missing
dependencies SHALL yield an actionable error. The CLI SHALL compose the host when a session starts —
the host's first use — for interactive, print and ACP sessions; `--version`, `--help` and
non-session commands SHALL NOT read the extension store. `--no-extensions` SHALL compose only the
built-in set, with no store scan, no third-party extension, and no third-party extension MCP server,
renderer or widget; configured `mcp.json` servers are unaffected. A discovery or load failure SHALL be logged
through the host log and SHALL NOT abort the session.

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

### Requirement: Extension tools

Extensions SHALL register tools through `IExtensionContext.RegisterTool` at creation time only —
registration SHALL NOT start processes, sockets or timers — with a registration carrying a local
name, a description, a hand-written JSON `ParametersSchema`, optional `OutputSchema`, optional
`ToolAnnotations`, a `ToolExposure` (Direct default), an optional explicit `ToolRisk` override
(otherwise derived from the annotations as for core tools) and an `IExtensionToolHandler`. The
handler SHALL receive the same `ToolContext` and return the same `ToolResult` as core tools — one
tool model, including `ctx.FileChanges`, `ctx.RunId`, `ctx.WorkingDirectory` and nested
`ExecuteToolAsync` with `"<parent>/<n>"` ids and the core's depth limits. The model-facing name SHALL
be the manifest alias when one is declared, otherwise `<extension-id>__<local-name>`, sanitized to
`[A-Za-z0-9_-]` and at most 64 characters (no `:`); core tool names stay reserved; the namespace
(discovery grouping) SHALL be the extension id. Names SHALL be unique: duplicates within the
extension, across extensions, or against a core name SHALL be refused naming both sources. A
composition-time collision between an extension tool and an MCP tool's model-facing name SHALL
resolve first-composition-wins: extension tools compose before MCP tools, and the later tool SHALL be
skipped with one single-line diagnostic naming both sources; malformed declarations (blank or
invalid name, non-object schema, missing handler) SHALL be refused naming the extension and the
field; registrations per extension SHALL be bounded at 32. The manifest `tools` list SHALL be
enforced asymmetrically: a registered tool that the manifest does not declare SHALL refuse the load
(the manifest is what the user trusted before any code loaded), while a declared tool that is not
registered SHALL be logged as a warning and dropped from the offered set. A tool's declared
`toolActivation` rule SHALL decide whether it enters a run's registry (see *Conditional tool
availability*). Registered tools SHALL enter the same `ToolRegistry` as core tools and behave
identically: the exposure gate governs declaration and nested/programmatic execution, annotations
reach approval, a throwing handler becomes a teaching error result, and unload removes the tool and
cancels in-flight calls into a cancelled result.

#### Scenario: A registered tool is declared and runs

- **GIVEN** an extension registering a tool with a hand-written schema
- **WHEN** a session composes and the model calls the tool
- **THEN** the declaration carries the prefixed name (`<extension-id>__<name>`), the description and
  the authored schema, and the handler executes with the model's arguments and the real
  `ToolContext`

#### Scenario: An alias replaces the model-facing name

- **GIVEN** an extension tool with a declared alias
- **WHEN** the declaration is built
- **THEN** the alias is the model-facing name, while the extension id and local name remain the
  identity used by diagnostics, grouping and unload

#### Scenario: Annotations reach the ToolCalling hook

- **GIVEN** an extension tool declared `Destructive`
- **WHEN** a call to it dispatches
- **THEN** the `ToolCalling` payload lists the annotations and a policy handler can block the call
  on that basis

#### Scenario: Missing hints ask

- **GIVEN** an extension tool registered without annotations
- **WHEN** a call to it dispatches
- **THEN** its derived risk is not read-only, and the approval policy asks

#### Scenario: Exposure gates are respected

- **GIVEN** extension tools registered `Hidden` and `Programmatic`
- **WHEN** the model's declarations are computed
- **THEN** neither is declared, and only programmatic or nested execution can reach them

#### Scenario: Duplicates and malformed declarations are refused

- **GIVEN** an extension registering a duplicate name or a malformed declaration
- **WHEN** it registers
- **THEN** the registration is refused with an error naming the sources, the extension and the field

#### Scenario: The manifest mismatch rule is asymmetric

- **GIVEN** an extension whose manifest does not declare a tool it registers
- **WHEN** it loads
- **THEN** the load is refused; and a declared but unregistered tool name is logged as a warning and
  dropped from the offered set

#### Scenario: A colliding MCP tool is skipped with a diagnostic

- **GIVEN** an extension tool and an MCP tool whose model-facing names collide
- **WHEN** the session composes tools
- **THEN** the extension tool is registered, the MCP tool is skipped with one single-line diagnostic
  naming both sources, and the session continues

#### Scenario: The context publishes file changes and runs nested calls

- **GIVEN** an extension tool whose handler publishes a file change and makes a nested call
- **WHEN** it executes
- **THEN** the publish lands on the bus with the full payload (absolute and relative path, workspace
  id, run id, kind) and the nested call runs through the same pipeline with a `"<parent>/<n>"` id

#### Scenario: A handler failure teaches, unload cancels

- **GIVEN** a handler that throws, and a second call in flight when the extension unloads
- **WHEN** they settle
- **THEN** the throw becomes an error result with instructing text (never a crash), and the in-flight
  call is cancelled and settles as a cancelled result while the extension's tools leave the registry
  with `ToolsChanged` fired

#### Scenario: A resumed session renders a missing tool

- **GIVEN** a resumed session containing a call to an extension tool that is not present
- **WHEN** the call is rendered
- **THEN** it renders through the default view without a registered renderer and without a crash

#### Scenario: An inactive tool is absent from the run

- **GIVEN** a declared extension tool whose activation rule is false for the run's workspace
- **WHEN** the run's tool registry is composed
- **THEN** the tool is not declared, not nested-callable and absent from the programmatic stubs, while
  it remains registered in the extension registry and visible in `/extensions` as inactive
