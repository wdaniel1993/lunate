## ADDED Requirements

### Requirement: Decision layer activation

Decision consumption SHALL be configured by one `decisions` block, with the same shape for every
consumer. The block names the `profile` the layer runs on and each consumer's opt-in: `approvals`
(`off` default), `approvalsThreshold`, `routing` (`off` default), `routingProfiles`, `tool` (`off`
default) and `questions` (`off` default). Without a `profile` the layer is unavailable and nothing
is offered; enabling a consumer without one is not an error. No consumer acts by default, and a
configured decision profile SHALL be required for any decision to be proposed — the layer never
borrows the chat profile. The old flat keys (`decisionMode`, `routingMode`, `routingProfiles`) and
the `usages.decision` assignment are refused like any unknown key. Validation SHALL refuse, with a
message naming the field: unknown keys, unknown consumer literals, an out-of-range
`approvalsThreshold` (a finite number from 0 through 1), unknown profile names behind
`decisions.profile` or `routingProfiles`, fewer than two `routingProfiles` when `routing` is on, and
more than 255. Settings SHALL name only consumers this version defines: model routing, the approval
gate, question suggestions and the `decide` tool. Settings SHALL NOT be able to name an acting
decision consumer whose subject this version does not define.

#### Scenario: Opt-in settings activate shadow consumption

- **GIVEN** a user who opts into shadow-mode consumption for the approval gate
- **WHEN** a session records a decision
- **THEN** the consumer records its would-have result, and execution runs the ordinary approval flow
  regardless of the decision

#### Scenario: No decision profile means off, not error

- **GIVEN** settings that enable a decision consumer but configure no `decisions.profile`
- **WHEN** the session starts and asks for approvals
- **THEN** decisions are reported unavailable, no model call is made for the decision layer, and the
  `decide` tool is not offered

#### Scenario: An unknown acting consumer is refused by settings

- **GIVEN** settings naming an acting decision consumer that no capability defines
- **WHEN** the settings are validated
- **THEN** they are rejected with an error naming the consumer, before any session runs

#### Scenario: The decision block is user-scoped

- **GIVEN** project settings that carry the `decisions` block
- **WHEN** the settings are validated
- **THEN** they are rejected; decision consumers are enabled by the user, never by a repository

### Requirement: The four consumers are separately configured

Each consumer SHALL have its own setting inside the `decisions` block, and enabling one SHALL NOT
enable another. The approval gate (`approvals`) and model routing (`routing`) SHALL each offer a
shadow setting as the user's try-out; question suggestions (`questions`) SHALL be display-only when
enabled; the `decide` tool (`tool`) SHALL be offered only when `profile` resolves and `tool` is on.
A deny recommendation that reaches `approvalsThreshold` SHALL escalate the approval ask; a deny
below it SHALL leave the ordinary approval flow unchanged, and routing SHALL pick the most probable
profile without consulting any threshold.

#### Scenario: Enabling routing does not enable the approval gate

- **GIVEN** settings that enable model routing only
- **WHEN** a tool call needs approval
- **THEN** the decision layer evaluates nothing for that ask, and the ordinary approval flow runs

#### Scenario: The decide tool needs its own switch

- **GIVEN** `decisions.tool` set to `on` with no `decisions.profile`
- **WHEN** the tool list is assembled
- **THEN** the `decide` tool is not offered and the layer reports unavailable without an error state
