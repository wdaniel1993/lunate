## MODIFIED Requirements

### Requirement: Local endpoints without a dummy key

The factory SHALL send a provider's environment API key only to the provider's default endpoint: when a model declares a custom endpoint without an explicit credential reference, both the OpenAI-compatible and the Anthropic adapter SHALL construct their clients with a placeholder credential instead of the environment key, and SHALL NOT fail for a missing key — so local servers work without dummy environment variables and real keys are never sent to third-party endpoints. A model that spells out the provider's own default URL counts as a custom endpoint. A model MAY declare an explicit credential reference (`"auth": "<name>"` in `models.json`), the deliberate opt-in that resolves that named key from the user's auth store and sends it to the declared endpoint; a reference that cannot be resolved SHALL fail with an error naming the key. For models without a reference: default endpoints resolve the provider credential from the environment first, then the auth store, and fail with an actionable error when neither yields a key.

#### Scenario: Custom endpoint without a key

- **GIVEN** a model with a custom endpoint and no `OPENAI_API_KEY`
- **WHEN** the factory builds the pipeline
- **THEN** no error is raised and requests target the custom endpoint

#### Scenario: Custom endpoint never receives the environment key

- **GIVEN** a model with a custom endpoint and `OPENAI_API_KEY` set
- **WHEN** the factory builds the pipeline
- **THEN** the client uses the placeholder credential, not the environment key

#### Scenario: Explicit reference sends the named key

- **GIVEN** a model with a custom endpoint and `"auth": "work"` resolving to a stored key
- **WHEN** the factory builds the pipeline
- **THEN** the client is constructed with that key for the declared endpoint

#### Scenario: Unresolvable reference names the key

- **GIVEN** a model with `"auth": "missing"` and no such stored key
- **WHEN** the factory builds the pipeline
- **THEN** the error names `missing` and mentions auth.json, and no secret value appears

#### Scenario: The Anthropic adapter is symmetric

- **GIVEN** an Anthropic model with a custom endpoint and no `ANTHROPIC_API_KEY`
- **WHEN** the factory builds the pipeline
- **THEN** no error is raised and the client uses the placeholder credential

#### Scenario: Default endpoint requires a key from environment or store

- **GIVEN** a model without a custom endpoint and no key in the environment or auth store
- **WHEN** the factory builds the pipeline
- **THEN** an actionable error names the environment variable and auth.json
