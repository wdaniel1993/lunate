## ADDED Requirements

### Requirement: Tool contract
`Lunate.Agent` SHALL define tools as its own type: `ITool` with a name, a description, a hand-written JSON `ParametersSchema`, a `ToolRisk` (`ReadOnly`, `Write`, `Execute`) and `ExecuteAsync(JsonElement args, ToolContext ctx, CancellationToken ct)`; results as `ToolResult` (output, error flag, UI-only `Details`); and `ToolContext` carrying the working directory and the event sink. Schemas SHALL be authored by hand, never built by reflection.

#### Scenario: Contract is implementable without reflection
- **GIVEN** a tool implemented in a test with a hand-written schema
- **WHEN** it is exercised through the contract types
- **THEN** no reflection is involved and the result carries output, error flag and optional UI-only details

#### Scenario: Details never travel to the model
- **GIVEN** a tool result with `Details` set
- **WHEN** the loop appends the tool result to the conversation (T-09)
- **THEN** only the output and error flag reach the model; details are UI-only

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
