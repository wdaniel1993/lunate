## MODIFIED Requirements

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
