# agent-sessions Specification Delta

## ADDED Requirements

### Requirement: Question interactions remain paired in session messages

An `ask_user` request and its answer or unanswered outcome SHALL be preserved together in the assistant tool-call and tool-result messages already stored by the session. An MCP elicitation request and response SHALL be included as a labelled question/answer pair in its enclosing MCP tool result. Loading and replaying the session SHALL preserve interaction order and values without changing the session schema.

#### Scenario: A resumed session retains the question and answer

- **GIVEN** a session with an `ask_user` call and its answered tool result
- **WHEN** the session is loaded and its history is reconstructed
- **THEN** the assistant question call and matching answer result are both present in order with the original labels and text

#### Scenario: An unanswered request is also recorded as a pair

- **GIVEN** a session with an `ask_user` request that returns `unanswered: no user present`
- **WHEN** the session is loaded or exported
- **THEN** the question and its unanswered outcome remain paired and no schema migration is needed

#### Scenario: An MCP exchange is retained in its tool result

- **GIVEN** a session whose MCP tool call received a form elicitation from a server
- **WHEN** the tool result is recorded
- **THEN** it contains the server-labelled question and the user's response together in request order
