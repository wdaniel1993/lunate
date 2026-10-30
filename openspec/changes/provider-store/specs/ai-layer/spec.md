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

The model/provider catalog SHALL expose whether a resolved model supports structured output and declares decision capability. A provider MAY register the decision capability through the provider capability contract; capability resolution SHALL follow the selected provider/model metadata and SHALL NOT inspect vendor names. Missing metadata SHALL mean the capability is not declared.

#### Scenario: Capability declaration is vendor-neutral

- **GIVEN** a Jev-compatible provider registered with decision capability and structured output
- **WHEN** its model is resolved
- **THEN** the resolved metadata declares decision capability without a vendor-name check

#### Scenario: Missing capability is not inferred

- **GIVEN** a catalog model with no structured-output or decision-capability declaration
- **WHEN** the model is resolved
- **THEN** neither capability is inferred from its id or provider name

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
