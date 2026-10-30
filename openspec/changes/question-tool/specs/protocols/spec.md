# protocols Specification Delta

## ADDED Requirements

### Requirement: MCP form elicitation uses the shared question interaction

The MCP client SHALL advertise `elicitation.form` and handle MCP `elicitation/create` form requests through the same question interaction used by `ask_user`. It SHALL accept the protocol's `message` and flat-object `requestedSchema`, including supported primitive fields, single-select enums and multi-select enum arrays; answers SHALL be validated against the requested schema and returned using MCP's `accept`, `decline` or `cancel` response shape. It SHALL identify the requesting MCP server in the user-facing question and SHALL NOT create a second picker implementation. If no user interaction is available, it SHALL return `cancel` immediately and record the unanswered outcome in the enclosing tool result so the model can state an assumption and continue. V1 SHALL NOT advertise URL-mode elicitation.

#### Scenario: A server's form request is answered through the shared picker

- **GIVEN** an MCP server sends `elicitation/create` with `mode: "form"`, a message and a flat `requestedSchema` containing an enum field
- **WHEN** the user selects a valid value and submits
- **THEN** the shared question interaction returns an `accept` response whose content validates against the requested schema and identifies the requesting server

#### Scenario: A multi-select form returns its selected values

- **GIVEN** an MCP form field whose schema is an array of strings constrained by an enum
- **WHEN** the user selects multiple values and submits
- **THEN** the client returns those values as an array in the accepted content

#### Scenario: A skipped MCP form is cancelled, not approved

- **GIVEN** an MCP server's form elicitation is open
- **WHEN** the user presses Esc
- **THEN** the client returns an MCP `cancel` response without content and no tool approval is granted

#### Scenario: An unattended MCP form does not block

- **GIVEN** an MCP server requests form elicitation during a print or unattended run
- **WHEN** no user-input mechanism is available
- **THEN** the client immediately returns `cancel` without content, records `unanswered: no user present` in the enclosing tool result, and the model is directed to state an assumption and continue

#### Scenario: URL-mode elicitation is not advertised or presented as a form

- **GIVEN** an MCP server connected to Lunate
- **WHEN** it inspects client capabilities or sends an unsupported URL-mode request
- **THEN** Lunate advertises form mode only and refuses the unsupported request as invalid parameters without opening a URL

### Requirement: ACP questions use only a non-approval input mechanism

In ACP mode, a question SHALL use a client mechanism explicitly intended for user input when the client advertises one. Lunate SHALL NOT use `session/request_permission` to collect an answer. If no such mechanism is available, the question SHALL return immediately as `unanswered: no user present`.

#### Scenario: An ACP user-input capability receives the question

- **GIVEN** an ACP client advertises a mechanism for non-approval user input
- **WHEN** `ask_user` runs
- **THEN** the question is sent through that mechanism and its answer is returned as information, not permission

#### Scenario: Permission requests are not reused for questions

- **GIVEN** an ACP client advertises `session/request_permission` but no user-input mechanism
- **WHEN** `ask_user` runs
- **THEN** no permission request is sent and the tool immediately returns `unanswered: no user present`
