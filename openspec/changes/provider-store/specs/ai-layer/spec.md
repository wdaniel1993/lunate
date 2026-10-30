# AI Layer Specification Delta

## ADDED Requirements

### Requirement: Provider templates and user instances

The twelve built-in provider presets SHALL be provider-kind templates that supply adapter and default configuration, not a closed set of provider instances. The template kinds SHALL be `openai`, `anthropic`, `openrouter`, `deepseek`, `moonshotai`, `zai`, `opencode`, `opencode-go`, `groq`, `togetherai`, `xai`, and `google`. The provider resolver SHALL accept multiple named user instances of one kind and use each instance's declared base URL, credential reference, headers, and region. A provider instance SHALL use the adapter selected by its template kind; changing its endpoint SHALL NOT silently change its adapter.

#### Scenario: Two providers use one kind independently

- **GIVEN** two provider instances of kind `openai` with different base URLs and credential references
- **WHEN** each model profile is resolved
- **THEN** each client uses only its named provider instance's endpoint and credential

#### Scenario: Preset templates remain usable without instances

- **GIVEN** the built-in provider templates and no user-created instance for one template
- **WHEN** a legacy preset model resolves
- **THEN** it continues to use the template's existing adapter and default configuration

### Requirement: Model capability metadata

The model/provider catalog SHALL expose an optional structured-output capability flag and optional `apiType` metadata (`chat` or `decision`). At configuration time, only an explicit structured-output=false flag SHALL refuse decision assignment; unknown capability SHALL proceed to runtime validation. `apiType` SHALL be a labelling hint only and SHALL NOT gate decision usage. A provider MAY declare a provider-calibrated decision API through metadata. An explicitly assigned chat model with structured-output support MAY serve decision usage as an `adapter`, directly or through a wrapper. Capability SHALL NOT be inferred from vendor names or ids. Decision method, probability method and calibrated state SHALL be distinct: every model starts uncalibrated and only per-model shadow evidence plus maintainer sign-off may mark it calibrated.

#### Scenario: A provider-calibrated decision API is labelled vendor-neutrally

- **GIVEN** a Jev-compatible provider whose API metadata declares a provider-calibrated decision API and structured output
- **WHEN** its model is resolved
- **THEN** the resolved metadata labels it as `native` without a vendor-name check

#### Scenario: Explicitly absent structured output refuses decision assignment

- **GIVEN** a catalog model with structured-output=false
- **WHEN** it is assigned to decision
- **THEN** assignment is refused by the explicit capability flag, without vendor, id or API-type inference

#### Scenario: Unknown structured-output capability is allowed

- **GIVEN** a catalog model whose structured-output capability is unknown
- **WHEN** it is assigned to decision
- **THEN** configuration allows it and runtime validates its response contract

#### Scenario: A chat model with structured output can be an adapter

- **GIVEN** a chat-typed catalog model that supports structured output
- **WHEN** it is explicitly assigned to decision
- **THEN** it is labelled `adapter` and uncalibrated rather than refused by its API type

### Requirement: Profile options are validated against resolved models

The model layer SHALL validate each profile option against the resolved catalog model and provider before constructing a client. It SHALL reject an unsupported setting by its field name and profile name, rather than silently removing it. The effective context window SHALL use a profile override only after validation, and otherwise SHALL remain the catalog/provider value.

#### Scenario: Unsupported option fails model resolution

- **GIVEN** a profile with an option unsupported by its selected model
- **WHEN** a session resolves its profiles
- **THEN** resolution fails with the option and profile names before a client request

#### Scenario: Context override is explicit

- **GIVEN** a profile with a valid context-window override
- **WHEN** the client model metadata is composed
- **THEN** the profile's validated window is effective and the catalog entry remains unchanged
