## ADDED Requirements

### Requirement: Extension tools

Extensions SHALL register tools through `IExtensionContext.RegisterTool` at creation time only — registration SHALL NOT start processes, sockets or timers — with a registration carrying a name, a description, a hand-written JSON `ParametersSchema`, optional `OutputSchema`, annotations (`ReadOnly`, `Destructive`, `Idempotent`, `OpenWorld`), an exposure, an optional explicit risk override (otherwise derived from the annotations as for core tools), an optional namespace (defaulting to the extension id) and an `IExtensionToolHandler`. Names SHALL be unique across the registry: a duplicate within the extension, across extensions or against a core tool name SHALL be refused with an error naming both sources; malformed declarations (blank or invalid name, non-object schema, missing handler) SHALL be refused naming the extension and the field; registrations per extension SHALL be bounded at 32. A manifest `tools` list that does not match the registrations SHALL be logged as a warning, not refused. Registered tools SHALL enter the same `ToolRegistry` as core tools and SHALL behave identically: the exposure gate governs declaration and nested/programmatic visibility, annotations reach the `ToolCalling` payload, the approval policy applies, results are bounded and handler exceptions become error results with instructing text. The handler SHALL receive an `IExtensionToolContext` exposing the working directory, the calling run id, a file-change publish sink (the same stamped sink core tools use) and nested `ExecuteToolAsync` going through the same pipeline with `"<parent>/<n>"` ids and the core's depth limits. The API version SHALL move as a semver-minor addition.

#### Scenario: A registered tool is declared and runs

- **GIVEN** an extension registering a tool with a hand-written schema
- **WHEN** a session composes and the model calls the tool
- **THEN** the declaration carries the authored name, description and schema, and the handler executes with the model's arguments

#### Scenario: Annotations reach the ToolCalling hook

- **GIVEN** an extension tool declared `Destructive`
- **WHEN** a call to it dispatches
- **THEN** the `ToolCalling` payload lists the annotations and a policy handler can block the call on that basis

#### Scenario: Exposure gates are respected

- **GIVEN** extension tools registered `Hidden` and `Programmatic`
- **WHEN** the model's declarations are computed
- **THEN** neither is declared, and only programmatic or nested execution can reach them

#### Scenario: Duplicates and malformed declarations are refused

- **GIVEN** an extension registering a tool named after a core tool
- **WHEN** it registers
- **THEN** the registration is refused with an error naming both sources; and a non-object schema is refused naming the extension and field

#### Scenario: The context publishes file changes and runs nested calls

- **GIVEN** an extension tool whose handler publishes a file change and makes a nested call
- **WHEN** it executes
- **THEN** the publish lands on the bus with the full payload (absolute and relative path, workspace id, run id, kind) and the nested call runs through the same pipeline with a `"<parent>/<n>"` id
