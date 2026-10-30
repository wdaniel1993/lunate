# print-mode Specification Delta

## ADDED Requirements

### Requirement: Questions do not block without a user

Print mode and unattended runs SHALL return `unanswered: no user present` from `ask_user` immediately when no user-interaction mechanism is available. MCP form elicitation in those runs SHALL be cancelled immediately and its enclosing tool result SHALL carry the same unanswered outcome. The result SHALL direct the model to continue on an explicitly stated assumption and SHALL NOT wait for input. Print mode's normal stdout contract SHALL remain unchanged; ACP SHALL use its own advertised non-approval input mechanism when available and otherwise use the same immediate unanswered result.

#### Scenario: Print mode continues immediately on an assumption

- **GIVEN** a print-mode run whose model calls `ask_user`
- **WHEN** no user is present
- **THEN** the tool immediately returns `unanswered: no user present`, tells the model to state its assumption and continue, and the run does not block

#### Scenario: Unattended execution never waits for a response

- **GIVEN** an unattended run with an `ask_user` call
- **WHEN** there is no configured user-input mechanism
- **THEN** the tool immediately returns `unanswered: no user present` and continues without reading stdin

#### Scenario: MCP elicitation is cancelled when unattended

- **GIVEN** a print-mode MCP tool call whose server requests form elicitation
- **WHEN** no user-input mechanism is available
- **THEN** the MCP request is cancelled immediately, its tool result records `unanswered: no user present`, and the model can state an assumption and continue without reading stdin

#### Scenario: Print output remains clean

- **GIVEN** a print-mode run that calls `ask_user`
- **WHEN** it completes without JSON event output
- **THEN** stdout contains only the final assistant answer and the unanswered result is not printed as a diagnostic
