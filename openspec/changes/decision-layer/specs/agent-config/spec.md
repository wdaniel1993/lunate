## ADDED Requirements

### Requirement: Decision layer activation

Decision consumption SHALL be controlled by per-consumer, opt-in settings; no consumer acts by
default. A configured `decision` usage profile SHALL be required for any decision to be proposed — the
layer never borrows the chat profile. When the user configures no `decision` usage profile, decision
consumers SHALL report the layer as unavailable without error state and SHALL stay off, and the `decide`
tool SHALL NOT be offered. Settings SHALL name only consumers this version defines: model routing,
the approval gate, question suggestions and the `decide` tool. Settings SHALL NOT be able to name an
acting decision consumer whose subject this version does not define.

#### Scenario: Opt-in settings activate shadow consumption

- **GIVEN** a user who opts into shadow-mode consumption for the approval gate
- **WHEN** a session records a decision
- **THEN** the consumer records its would-have result, and execution runs the ordinary approval flow
  regardless of the decision

#### Scenario: No decision profile means off, not error

- **GIVEN** settings that enable a decision consumer but configure no `decision` usage profile
- **WHEN** the session starts and asks for approvals
- **THEN** decisions are reported unavailable, no model call is made for the decision layer, and the
  `decide` tool is not offered

#### Scenario: An unknown acting consumer is refused by settings

- **GIVEN** settings naming an acting decision consumer that no capability defines
- **WHEN** the settings are validated
- **THEN** they are rejected with an error naming the consumer, before any session runs

### Requirement: The four consumers are separately configured

Each consumer SHALL have its own opt-in setting, and enabling one SHALL NOT enable another. Model
routing and the approval gate SHALL each offer a shadow setting as the user's try-out; question
suggestions SHALL be display-only when enabled; the `decide` tool SHALL be offered only when a
`decision` usage profile exists and its setting is enabled.

#### Scenario: Enabling routing does not enable the approval gate

- **GIVEN** settings that enable model routing only
- **WHEN** a tool call needs approval
- **THEN** the decision layer evaluates nothing for that ask, and the ordinary approval flow runs
