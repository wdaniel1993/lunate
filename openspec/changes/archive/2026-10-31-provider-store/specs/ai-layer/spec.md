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

Each model descriptor SHALL carry an extensible capability set with API names (`chat`, `decision`, `image-generation`, `embeddings`, and future strings), input modalities, output modalities, and features (`tools`, `structured-output`, `reasoning`, and future strings). These names SHALL be strings, not a closed enum. Each category MAY be unknown; an omitted category SHALL NOT be interpreted as unsupported. Capability facts SHALL layer in this order: catalog, provider discovery, then the user's profile override. A later source SHALL replace only categories it explicitly supplies; omitted categories retain the previous fact or remain unknown.

#### Scenario: A future API needs no enum change

- **GIVEN** a catalog model descriptor that includes API `embeddings`
- **WHEN** the descriptor is loaded
- **THEN** the API name is retained without changing a closed enum

#### Scenario: Capability sources layer in order

- **GIVEN** catalog, provider-discovery and user-profile facts for the same capability category
- **WHEN** the profile resolves
- **THEN** the user-profile fact overrides discovery, discovery overrides catalog, and omitted categories remain inherited or unknown

#### Scenario: Unknown capability is allowed

- **GIVEN** a model whose capability category is unknown from every source
- **WHEN** a usage profile resolves
- **THEN** the host allows it rather than inferring that the capability is absent

#### Scenario: A chat model can serve a user-selected decision usage

- **GIVEN** a user assigns a chat model with known `structured-output` feature to decision
- **WHEN** the profile resolves
- **THEN** it is accepted and the request method is labelled `adapter`

#### Scenario: API metadata labels but does not gate decision use

- **GIVEN** a user-selected model with API `chat` and the required `structured-output` feature
- **WHEN** it serves decision usage
- **THEN** it is labelled `adapter` and is not rejected by its API name

#### Scenario: Image input is checked only when an image is attached

- **GIVEN** an image attachment and a chat model whose known input modalities exclude `image`
- **WHEN** the request is prepared
- **THEN** it is refused before sending with a notice naming the model and saying to switch profile or remove the image

#### Scenario: Unknown image input is attempted and provider rejection is translated

- **GIVEN** an image attachment and a chat model whose input modalities are unknown
- **WHEN** the request is sent and the provider rejects the image
- **THEN** the user receives the same clear model-named switch-profile-or-remove-image notice, not a raw provider error

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
