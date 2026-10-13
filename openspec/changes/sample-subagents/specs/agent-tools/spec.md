## MODIFIED Requirements

### Requirement: Tool contract

`Lunate.Agent` SHALL define tools as its own type: `ITool` with a name, a description, a hand-written JSON `ParametersSchema`, a `ToolRisk` (`ReadOnly`, `Write`, `Execute`) and `ExecuteAsync(JsonElement args, ToolContext ctx, CancellationToken ct)`; results as `ToolResult` (output, error flag, UI-only `Details`); and `ToolContext` carrying the working directory, an event sink that accepts only extension events, and the per-run subagent seam (`Subagents`, see the agent-loop specification — it starts child runs with an optional allow-list of the run's own tools and only-tightening limits) stamped by the harness alongside the run id and file-change sink. Schemas SHALL be authored by hand, never built by reflection. Error results SHALL reach the model through their instructing output text; a provider-level error flag is not sent (revisit when real tools and a live fixture exist).

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
