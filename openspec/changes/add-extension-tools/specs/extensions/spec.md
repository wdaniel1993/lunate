## ADDED Requirements

### Requirement: Extension tools

Extensions SHALL register tools through `IExtensionContext.RegisterTool` at creation time only — registration SHALL NOT start processes, sockets or timers — with a registration carrying a local name, a description, a hand-written JSON `ParametersSchema`, optional `OutputSchema`, optional `ToolAnnotations`, a `ToolExposure` (Direct default), an optional explicit `ToolRisk` override (otherwise derived from the annotations as for core tools) and an `IExtensionToolHandler`. The handler SHALL receive the same `ToolContext` and return the same `ToolResult` as core tools — one tool model, including `ctx.FileChanges`, `ctx.RunId`, `ctx.WorkingDirectory` and nested `ExecuteToolAsync` with `"<parent>/<n>"` ids and the core's depth limits. The model-facing name SHALL be `<extension-id>__<local-name>`, sanitized to `[A-Za-z0-9_-]` and at most 64 characters (no `:`); core tool names stay reserved; the namespace (discovery grouping) SHALL be the extension id. Names SHALL be unique: duplicates within the extension, across extensions, against a core name or against an MCP tool's model-facing name SHALL be refused naming both sources — except that a composition-time collision between an extension tool and a later-composed MCP tool SHALL resolve first-composition-wins: extension tools compose before MCP tools, and the later tool SHALL be skipped with one single-line diagnostic naming both sources; malformed declarations (blank or invalid name, non-object schema, missing handler) SHALL be refused naming the extension and the field; registrations per extension SHALL be bounded at 32. The manifest `tools` list SHALL be enforced asymmetrically: a registered tool that the manifest does not declare SHALL refuse the load (the manifest is what the user trusted before any code loaded), while a declared tool that is not registered SHALL be logged as a warning and dropped from the offered set. Registered tools SHALL enter the same `ToolRegistry` as core tools and behave identically: the exposure gate governs declaration and nested/programmatic visibility, annotations reach the `ToolCalling` payload, the approval policy applies with MCP defaults when no hints are declared (not read-only, possibly destructive — the call asks), results are bounded, and a throwing handler becomes an error result with instructing text, never a crash; cancellation SHALL be cooperative and linked to the extension's lifetime (a handler that ignores its token runs to completion). Unloading an extension SHALL remove its tools from the registry, fire `ToolsChanged` and cancel its in-flight calls; a resumed session containing calls to an absent extension tool SHALL still render. The API version SHALL move as a semver-minor addition.

#### Scenario: A registered tool is declared and runs

- **GIVEN** an extension registering a tool with a hand-written schema
- **WHEN** a session composes and the model calls the tool
- **THEN** the declaration carries the prefixed name (`<extension-id>__<name>`), the description and the authored schema, and the handler executes with the model's arguments and the real `ToolContext`

#### Scenario: Annotations reach the ToolCalling hook

- **GIVEN** an extension tool declared `Destructive`
- **WHEN** a call to it dispatches
- **THEN** the `ToolCalling` payload lists the annotations and a policy handler can block the call on that basis

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
- **THEN** the load is refused; and a declared but unregistered tool name is logged as a warning and dropped from the offered set

#### Scenario: A colliding MCP tool is skipped with a diagnostic

- **GIVEN** an extension tool and an MCP tool whose model-facing names collide
- **WHEN** the session composes tools
- **THEN** the extension tool is registered, the MCP tool is skipped with one single-line diagnostic naming both sources, and the session continues

#### Scenario: The context publishes file changes and runs nested calls

- **GIVEN** an extension tool whose handler publishes a file change and makes a nested call
- **WHEN** it executes
- **THEN** the publish lands on the bus with the full payload (absolute and relative path, workspace id, run id, kind) and the nested call runs through the same pipeline with a `"<parent>/<n>"` id

#### Scenario: A handler failure teaches, unload cancels

- **GIVEN** a handler that throws, and a second call in flight when the extension unloads
- **WHEN** they settle
- **THEN** the throw becomes an error result with instructing text (never a crash), and the in-flight call is cancelled and settles as a cancelled result while the extension's tools leave the registry with `ToolsChanged` fired

#### Scenario: A resumed session renders a missing tool

- **GIVEN** a resumed session containing a call to an extension tool that is not present
- **WHEN** the call is rendered
- **THEN** it renders through the default view without a registered renderer and without a crash

## MODIFIED Requirements

### Requirement: Extension contract assembly

Extensions SHALL compile against `Lunate.Extensibility.Abstractions` — the contract's own surface and the `Lunate.Agent` types its public signatures expose (`ToolContext`, `ToolResult`, `ToolAnnotations`, `ToolExposure`, `ToolRisk`) — and `Microsoft.Extensions.AI.Abstractions`; never against other `Lunate.Agent` internals and never against other Lunate assemblies. The contract assembly SHALL be semver'd with a PublicAPI-tracked surface, and its entry point SHALL be `IExtensionFactory.Create(IExtensionContext)` returning an `IExtension`. The context SHALL expose the extension id, settings, secrets and a log sink. The current API version SHALL be a single constant, and the assembly SHALL depend on no packages beyond the MEAI abstractions, and no Lunate assemblies beyond the one-way `Lunate.Agent` reference that carries the tool contract.

#### Scenario: An extension compiles against the contract only

- **GIVEN** an extension project referencing the contract assembly
- **WHEN** it is built
- **THEN** it compiles using the contract and the `Lunate.Agent` types the contract exposes, and referencing nothing beyond them

#### Scenario: The API version is queryable

- **GIVEN** the contract assembly
- **WHEN** the current API version is read
- **THEN** it is a single semver constant that the range matcher validates against

### Requirement: Extension loading

The host SHALL discover extensions in `~/.lunate/extensions/` (global) and `.lunate/extensions/` (project), reading manifests only — assemblies SHALL load lazily on first use into a collectible `AssemblyLoadContext` that shares the contract assemblies — `Lunate.Extensibility.Abstractions` and the `Lunate.Agent` types it references — and `Microsoft.Extensions.AI.Abstractions` and `System.Text.Json` with the default context, and isolates every other dependency. Unload SHALL be idempotent; a later load SHALL work; state SHALL not survive. Duplicate ids across scopes SHALL be an error naming both paths. Missing dependencies SHALL yield an actionable error. The CLI SHALL compose the host when a session starts — the host's first use — for interactive, print and ACP sessions; `--version`, `--help` and non-session commands SHALL NOT read the extension store. `--no-extensions` SHALL start every mode without composing the host (extension hooks, renderers, widgets and extension MCP servers absent; configured `mcp.json` servers unaffected). A discovery or load failure SHALL be logged through the host log and SHALL NOT abort the session.

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
- **THEN** the extension is discovered and loaded as the session starts, and its hook observes the session's tool calls

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
- **THEN** the extension store is not read, its hook never runs, and the configured MCP server still works
