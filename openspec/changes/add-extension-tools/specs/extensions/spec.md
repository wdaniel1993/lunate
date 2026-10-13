## ADDED Requirements

### Requirement: Extension tools

Extensions SHALL register tools through `IExtensionContext.RegisterTool` at creation time only — registration SHALL NOT start processes, sockets or timers — with a registration carrying a local name, a description, a hand-written JSON `ParametersSchema`, optional `OutputSchema`, optional `ToolAnnotations`, a `ToolExposure` (Direct default), an optional explicit `ToolRisk` override (otherwise derived from the annotations as for core tools) and an `IExtensionToolHandler`. The handler SHALL receive the same `ToolContext` and return the same `ToolResult` as core tools — one tool model, including `ctx.FileChanges`, `ctx.RunId`, `ctx.WorkingDirectory` and nested `ExecuteToolAsync` with `"<parent>/<n>"` ids and the core's depth limits. The model-facing name SHALL be `<extension-id>__<local-name>`, sanitized to `[A-Za-z0-9_-]` and at most 64 characters (no `:`); core tool names stay reserved; the namespace (discovery grouping) SHALL be the extension id. Names SHALL be unique: duplicates within the extension, across extensions or against a core name SHALL be refused naming both sources; malformed declarations (blank or invalid name, non-object schema, missing handler) SHALL be refused naming the extension and the field; registrations per extension SHALL be bounded at 32. The manifest `tools` list SHALL be enforced asymmetrically: a registered tool that the manifest does not declare SHALL refuse the load (the manifest is what the user trusted before any code loaded), while a declared tool that is not registered SHALL be logged as a warning and dropped from the offered set. Registered tools SHALL enter the same `ToolRegistry` as core tools and behave identically: the exposure gate governs declaration and nested/programmatic visibility, annotations reach the `ToolCalling` payload, the approval policy applies with MCP defaults when no hints are declared (not read-only, possibly destructive — the call asks), results are bounded, and a throwing or timed-out handler becomes an error result with instructing text, never a crash. Unloading an extension SHALL remove its tools from the registry, fire `ToolsChanged` and cancel its in-flight calls; a resumed session containing calls to an absent extension tool SHALL still render. The API version SHALL move as a semver-minor addition.

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
