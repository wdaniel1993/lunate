## ADDED Requirements

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

## MODIFIED Requirements

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
