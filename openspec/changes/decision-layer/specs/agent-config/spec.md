## ADDED Requirements

### Requirement: Decision layer activation
Decision consumption SHALL be controlled by per-consumer, opt-in settings; no consumer acts by default. A configured `decision` usage profile SHALL be required for any decision to be proposed — the layer never borrows the chat profile. When the user configures no `decision` usage profile, decision consumers SHALL report the layer as unavailable without error state and SHALL stay off. Settings SHALL NOT be able to name an acting decision consumer whose subject this version does not define.

#### Scenario: Opt-in settings activate shadow consumption
- **GIVEN** a user who opts into shadow-mode consumption for a consumer this version defines
- **WHEN** a session records a decision
- **THEN** the consumer records its would-have result, and execution runs the ordinary approval flow regardless of the decision

#### Scenario: No decision profile means off, not error
- **GIVEN** settings that enable a decision consumer but configure no `decision` usage profile
- **WHEN** the session starts and asks for approvals
- **THEN** decisions are reported unavailable and no model call is made for the decision layer

#### Scenario: An unknown acting consumer is refused by settings
- **GIVEN** settings naming an acting decision consumer that no capability defines
- **WHEN** the settings are validated
- **THEN** they are rejected with an error naming the consumer, before any session runs
