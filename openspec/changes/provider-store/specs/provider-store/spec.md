# Provider Store Specification Delta

## ADDED Requirements

### Requirement: User-scoped provider entries

The provider store SHALL define providers only in user scope. Each provider entry SHALL have a unique user-facing name, a kind selected from the twelve existing templates (`openai`, `anthropic`, `openrouter`, `deepseek`, `moonshotai`, `zai`, `opencode`, `opencode-go`, `groq`, `togetherai`, `xai`, or `google`), a base URL, a credential reference, optional headers, and optional region (`eu`, `us`, or `local`). Multiple entries MAY use the same kind. A credential reference SHALL identify an auth-store key, environment variable, or configured helper. Header values SHALL be credential references of the same forms, never literal secret values. Provider and project settings SHALL never contain resolved credentials or header values. The `region` value SHALL be preserved as metadata; this change SHALL NOT route data or enforce a region policy.

#### Scenario: Multiple entries share a template kind

- **GIVEN** two user provider entries with different names and kind `openai`
- **WHEN** the provider store loads
- **THEN** both entries resolve independently with their own base URL, credential reference, headers, and region

#### Scenario: Region is retained without routing policy

- **GIVEN** a provider entry with region `eu`
- **WHEN** a profile uses that provider
- **THEN** the region remains available as provider metadata and does not select an endpoint or route a request

#### Scenario: Provider files contain references, never secrets

- **GIVEN** an auth key, environment variable, or helper resolves to a credential or header value
- **WHEN** provider settings are written or listed
- **THEN** only references are stored or shown and neither resolved value is present

### Requirement: Provider listing and credential migration

`lunate auth list` SHALL list provider entries with credential present/absent and endpoint reachability (`reachable`, `unreachable`, or `unknown`), without printing credential values or header values. Reachability SHALL be a bounded endpoint-connectivity check that does not send a credential or optional header. Loading an existing auth store SHALL automatically create the matching provider entry for each stored key using its existing preset or custom provider definition; migration SHALL preserve the key in the auth store and SHALL NOT replace an explicit provider entry. Provider environment overrides SHALL continue to take precedence over stored credentials, including in CI.

#### Scenario: Auth list reports providers safely

- **GIVEN** one reachable provider with a credential, one unreachable provider without one, and a header value
- **WHEN** `lunate auth list` runs
- **THEN** both providers show credential presence and reachability, and neither the header value nor any credential value is printed

#### Scenario: Existing stored keys create provider entries

- **GIVEN** a legacy auth store with a stored key and no matching provider entry
- **WHEN** settings load and migration completes
- **THEN** the corresponding provider entry is created using the existing template/custom definition and the secret remains only in the auth store

#### Scenario: Explicit provider settings survive key migration

- **GIVEN** an existing stored key and a provider entry with the same name but a custom base URL
- **WHEN** migration runs
- **THEN** the explicit provider entry is preserved and the stored key remains referenced without its value being copied

#### Scenario: Environment credentials still win in CI

- **GIVEN** a migrated provider with an auth-store key and its environment override set
- **WHEN** a client resolves the credential
- **THEN** the environment value is used and the auth-store value remains unchanged

### Requirement: Named model profiles

A model profile SHALL map a unique profile name to one provider and one catalog model, with optional reasoning level or budget, maximum output, temperature, context-window override, and decision thresholds. One provider MAY back multiple profiles. Profile settings SHALL be validated against the selected catalog model and provider when configuration loads. An unsupported setting SHALL fail loading with the setting name and profile name; it SHALL NOT be silently dropped or ignored. Project profiles MAY reference an existing provider and define settings on top of it, but SHALL NOT create provider definitions.

#### Scenario: One provider backs multiple profiles

- **GIVEN** two profiles that name the same provider and different catalog models or settings
- **WHEN** both profiles load
- **THEN** both resolve independently against that provider

#### Scenario: Unsupported profile setting is named

- **GIVEN** a profile sets temperature on a model that does not support temperature
- **WHEN** profiles load
- **THEN** loading is refused with the profile name and `temperature`, and no model call starts

#### Scenario: A profile validates every configured setting

- **GIVEN** a profile with supported reasoning, output, temperature, context-window, and decision-threshold settings
- **WHEN** profiles load
- **THEN** the resolved settings match the validated catalog/provider capabilities and unsupported fields are not discarded

### Requirement: Usage-to-profile selection

Core selection SHALL resolve usages to profiles and profiles to providers; it SHALL NOT route an individual request among providers. The configured usages SHALL include `chat`, optional `fast`, `decision`, and defaults for `subagents` and `compaction`. `/model` SHALL switch the chat profile. An explicit `provider/model` SHALL resolve using that provider's default settings. A bare model id SHALL resolve only when exactly one provider/profile candidate matches; otherwise it SHALL be refused with every candidate named. A provider MAY perform its own per-request routing internally, as in the `model-router` sample; core SHALL add no routing DSL.

#### Scenario: Chat model selection changes only chat usage

- **GIVEN** a configured chat profile and other usage defaults
- **WHEN** `/model` selects another profile
- **THEN** chat switches to that profile and fast, decision, subagent, and compaction assignments remain unchanged

#### Scenario: Qualified model uses default settings

- **GIVEN** `provider-a/model-x` and a provider default profile with its settings
- **WHEN** the qualified model is selected ad hoc
- **THEN** it resolves through `provider-a` using the provider's default settings

#### Scenario: Ambiguous bare model id is refused with candidates

- **GIVEN** two providers expose the same bare model id
- **WHEN** that bare id is selected
- **THEN** selection is refused and the error names both `provider/model` candidates

#### Scenario: Routing stays inside a declared provider

- **GIVEN** a declared provider that routes requests internally like the `model-router` sample
- **WHEN** a usage resolves to its profile
- **THEN** core selects that provider once and does not apply a routing DSL or per-request provider selection

### Requirement: Decision capability, adapter, and degradation

The `decision` usage SHALL accept an explicitly assigned model profile unless the model metadata explicitly says structured output/schema generation is unsupported. Unknown capability SHALL be allowed at configuration time and validated at runtime. Catalog/discovery `apiType` SHALL be a labelling hint only and SHALL NOT gate acceptance; provider names and model ids SHALL NOT imply capability. A provider-calibrated decision API SHALL be labelled `native`; a chat model explicitly assigned to decision SHALL be labelled `adapter`, whether used directly or through the explicit `adapter:<provider>/<model>` wrapper. Every model SHALL start uncalibrated; calibrated state SHALL be earned per model id through shadow metrics and maintainer sign-off under #183, never inferred from method. Every adapter response SHALL be checked against the defined decision options and well-formed probabilities. If a provider exposes logprobs, they SHALL supply probability provenance; otherwise an adapter MAY use self-reported probability. Native decision APIs SHALL record native probability provenance. Decision method (`native` or `adapter`) and probability method (`logprobs`, `self-reported`, or `native`) SHALL be distinct fields. A well-formed probability result SHALL provide one finite value in [0, 1] per defined option and sum to 1 within the contract tolerance. Three consecutive contract failures SHALL mark that profile `cannot decide` for the rest of the session and name the model. The capability validator SHALL report `This model cannot decide`, naming the model and missing structured-output capability, only when the catalog explicitly says that capability is absent. Malformed output after capability passes SHALL report `invalid decision`. Decisions MAY tighten an outcome but SHALL never override a deny, regardless of calibration. If no decision profile is configured, the decision layer SHALL report unavailable and remain off; it SHALL NOT implicitly borrow chat.

#### Scenario: A provider-calibrated decision API is native without vendor matching

- **GIVEN** a provider/model whose metadata identifies a provider-calibrated decision API and structured output
- **WHEN** it is assigned to the decision usage
- **THEN** it is accepted and labelled `native` regardless of provider name

#### Scenario: A chat model can fulfil the contract as an adapter

- **GIVEN** a chat-typed model that supports structured output and is explicitly assigned to decision
- **WHEN** it is assigned to decision
- **THEN** it is accepted and labelled `adapter`, uncalibrated, whether used directly or through the explicit wrapper

#### Scenario: Explicitly absent structured output refuses assignment by model name

- **GIVEN** a model whose catalog explicitly declares structured-output support absent
- **WHEN** it is assigned to decision
- **THEN** assignment is refused with `This model cannot decide`, the model name, and missing structured-output capability before a model call

#### Scenario: Unknown structured-output capability is allowed for runtime validation

- **GIVEN** a model whose catalog has no structured-output capability value
- **WHEN** it is assigned to decision
- **THEN** configuration accepts the assignment and runtime validates the first response

#### Scenario: Repeated adapter contract failures disable the profile for the session

- **GIVEN** an adapter model returns three consecutive malformed decision contracts
- **WHEN** the third response is validated
- **THEN** the profile is marked `cannot decide` for the rest of the session and the notice names the model

#### Scenario: The wrapped chat adapter is explicit and uncalibrated

- **GIVEN** a structured-output chat model explicitly declared as `adapter:provider-a/model-x`
- **WHEN** it is selected for decision use
- **THEN** each decision is labelled `adapter`, carries model id `model-x`, and is marked uncalibrated, just like direct assignment of that chat model

#### Scenario: Invalid decision output is distinct from provider limitation

- **GIVEN** a capable decision model returns a response that violates the decision schema
- **WHEN** decision output is validated
- **THEN** the result is `invalid decision`, not `This model cannot decide`

#### Scenario: No decision profile leaves the layer unavailable

- **GIVEN** chat is configured and no decision profile is configured
- **WHEN** the decision layer initializes or is requested
- **THEN** it reports unavailable and stays off without invoking the chat profile

#### Scenario: Shadow metrics separate native and adapter kinds

- **GIVEN** shadow decisions from one native profile and one adapter profile
- **WHEN** shadow metrics are reported
- **THEN** each result remains grouped by method and model id, no models merge, and neither model is calibrated without its own shadow evidence and maintainer sign-off

#### Scenario: A decision never overrides a deny

- **GIVEN** a deny outcome and a calibrated decision profile
- **WHEN** the decision result is applied
- **THEN** the outcome remains denied

### Requirement: Usage and provider cost attribution

Every recorded model usage SHALL identify its usage (`chat`, `fast`, `decision`, `subagents`, or `compaction`), provider, resolved model id, and token lanes. Decision records SHALL additionally carry provider kind and model id. `/cost` SHALL report priced totals grouped by usage and by provider, retaining the existing rule that unknown pricing is unpriced rather than zero. Session schema 4 SHALL write this attribution and SHALL read schemas 1–3 without changing their interpretation; no credential reference or secret SHALL be written to a session.

#### Scenario: Cost is split by usage and provider

- **GIVEN** priced chat, fast, subagent, and compaction usage across two providers
- **WHEN** `/cost` is requested
- **THEN** it reports subtotals by usage and by provider and a total consistent with those subtotals

#### Scenario: Unknown pricing is not reported as zero

- **GIVEN** a usage record with a positive token lane whose rate is unknown
- **WHEN** `/cost` is requested
- **THEN** that amount is marked unpriced and is not shown as `$0`

#### Scenario: Decision provenance is recorded without credentials

- **GIVEN** a native or adapter decision using a provider profile
- **WHEN** its session record is written
- **THEN** it records provider kind and resolved model id and contains no credential reference or secret

#### Scenario: Earlier session schemas remain readable

- **GIVEN** a schema 1, 2, or 3 session
- **WHEN** the schema-4 reader loads it
- **THEN** it preserves the old model/usage interpretation and loads without inventing usage or provider attribution

### Requirement: Project provider-definition security boundary

Project settings MAY reference existing user providers and profiles, define project profiles on top of existing user providers, and lower user-configured limits. A project profile MAY reference an existing user profile. Project settings SHALL NOT define a provider, endpoint/base URL, or credential reference. A refused definition SHALL name the offending key and SHALL fail closed before resolving a secret or opening a connection. Providers SHALL be user-scoped because otherwise a repository could direct a provider to an attacker-controlled URL and receive the user's credential.

#### Scenario: Project profile references an existing provider

- **GIVEN** a project profile that references an existing user provider and lowers an output limit
- **WHEN** project settings load
- **THEN** the profile is accepted and the effective limit is the lower value

#### Scenario: Project settings reference an existing user profile

- **GIVEN** project settings that assign a usage to an existing user-defined profile
- **WHEN** project settings load
- **THEN** the reference resolves to that profile without creating or changing its provider definition

#### Scenario: Project cannot raise a user limit

- **GIVEN** a user limit and a higher project override
- **WHEN** project settings load
- **THEN** the override is refused by key and the user limit remains effective

#### Scenario: Attacker endpoint and credential reference are refused

- **GIVEN** a project settings file defining a provider with an attacker-controlled URL and a credential reference
- **WHEN** project settings load
- **THEN** loading is refused with the offending provider/endpoint/credential key named, and no secret is resolved or request sent
