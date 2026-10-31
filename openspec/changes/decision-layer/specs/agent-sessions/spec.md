## ADDED Requirements

### Requirement: Decision provenance in the session file
Decision metadata SHALL be persisted only through the schema-4 per-usage attribution contract approved in ADR-0027: for a `decision` usage the assistant usage metadata SHALL name the usage, profile name, provider name and kind, resolved model id, decision method (`native` or `adapter`) and probability. The session file SHALL NOT record the decision's option set, subject details, prompts to the decision model, provider configuration, credential references or secrets. A resumed session SHALL show recorded decision provenance as recorded, even if the profile or provider referenced by a historical decision later disappears from settings.

#### Scenario: A decision usage attribution round-trips on resume
- **GIVEN** a session whose schema-4 metadata records a `decision` usage with method `adapter` and probability 0.72
- **WHEN** the session is saved and resumed
- **THEN** the decision attribution is returned exactly as recorded, and the session shows no provider configuration or credential reference

#### Scenario: A profile deleted after a session keeps its provenance
- **GIVEN** a session with recorded decision provenance whose profile was later removed from settings
- **WHEN** the session is resumed
- **THEN** the historical attribution remains as recorded with a one-line notice naming the missing profile, and later decisions record the current resolution
