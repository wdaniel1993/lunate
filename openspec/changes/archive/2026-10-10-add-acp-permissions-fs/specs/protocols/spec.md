## MODIFIED Requirements

### Requirement: ACP server over stdio

`lunate --acp` SHALL speak the Agent Client Protocol over stdio so editors drive Lunate as their agent, behind an `IAcpServer` seam (`RunAsync(input, output, createHarness, ct)`) that keeps a later SDK switch cheap. The server SHALL implement `initialize` (protocol version 1, honest capabilities, negotiate down, log unsupported), session creation (a fresh harness per ACP session; the factory receives the session context — cwd, the client-backed approver, and the client file access when offered), `session/prompt` (one run; text and resource-link content blocks — resource links map to model-readable text, other kinds are logged and skipped; streaming `session/update` notifications mapped from `AgentEvent`s per the guide's event table; the response carries the stop reason) and `session/cancel` (cancels the in-flight run; the prompt response reports the cancellation; no updates follow it). Nothing but protocol frames may be written to stdout; logs go to stderr. The mode SHALL be mutually exclusive with print mode and the TUI (usage error, exit 2). The server SHALL be tested with an in-process client over a pipe pair.

#### Scenario: Initialize negotiates the protocol version

- **GIVEN** a client sending `initialize` with protocol version 1
- **WHEN** the server responds
- **THEN** the response carries version 1 and the implemented capabilities; a higher requested version negotiates down; unsupported capabilities are logged, not fatal

#### Scenario: A prompt streams updates in order and ends with the stop reason

- **GIVEN** a session and a prompt with a text block
- **WHEN** the run streams text and tool events
- **THEN** `session/update` notifications arrive in event order (agent message chunks, tool call, tool call update) and the prompt response carries the stop reason

#### Scenario: Cancel stops the run

- **GIVEN** a running prompt
- **WHEN** the client sends `session/cancel`
- **THEN** the run is cancelled, the prompt response reports the cancellation, and no updates follow it

#### Scenario: stdout carries only the protocol

- **GIVEN** `lunate --acp` started over stdio
- **WHEN** any diagnostics occur
- **THEN** stdout contains only protocol frames and every log line appears on stderr

#### Scenario: The mode conflicts are rejected

- **GIVEN** `lunate --acp` combined with `-p` or a prompt
- **WHEN** it starts
- **THEN** it exits with a usage error (exit 2) and speaks no protocol

## ADDED Requirements

### Requirement: ACP approvals, editor file system and resource links

In ACP mode, tool approvals SHALL compose the configured policy with the client's `session/request_permission`: calls the policy already allows SHALL run without prompting; everything else SHALL ask the editor with allow-once, allow-always and reject options — allow-once executes, allow-always executes and is remembered per tool for the session, reject declines as a tool error, and a cancel or request error resolves as a decline. Commands SHALL prompt under every policy. When the client advertises the file-system capability, the read, write and edit tools SHALL route file access through the client's `fs/read_text_file`/`fs/write_text_file` so editor buffers are respected (the client owns byte-order marks); without the capability they SHALL use the local disk. Prompt `ResourceLinkContent` blocks SHALL map to model-readable text: a `file://` URI inside the workspace becomes `@<relative path>`, anything else becomes `<name> (<uri>)`, in prompt order.

#### Scenario: Policy-allowed calls run unprompted

- **GIVEN** an ACP session under the ask policy and a read-only tool call
- **WHEN** the run executes it
- **THEN** no permission request is sent to the client

#### Scenario: A write asks the editor and allow-once executes

- **GIVEN** an ACP session under the ask policy and a write tool call
- **WHEN** the client answers allow-once
- **THEN** the request carried the tool call information and the write executed

#### Scenario: A rejection declines as a tool error

- **GIVEN** a permission request
- **WHEN** the client answers reject
- **THEN** the call is declined and surfaces as a tool error result, as a local decline does

#### Scenario: Allow-always is remembered for the session

- **GIVEN** a permission request answered allow-always
- **WHEN** the same tool is called again in the session
- **THEN** it runs without a further request

#### Scenario: Commands always prompt

- **GIVEN** an ACP session under any policy and a command tool call
- **WHEN** it runs
- **THEN** the client receives a permission request

#### Scenario: Cancelling during a request declines it

- **GIVEN** a permission request in flight
- **WHEN** the session is cancelled
- **THEN** the request resolves as a decline and nothing hangs

#### Scenario: Editor buffers are respected

- **GIVEN** a client advertising the file-system capability whose buffer differs from disk
- **WHEN** a read executes
- **THEN** it returns the client's buffer content, and a write goes to the client with the local file untouched

#### Scenario: Without the capability the disk is used

- **GIVEN** a client without the file-system capability
- **WHEN** file tools run
- **THEN** they read and write the local disk as before

#### Scenario: Resource links become readable text

- **GIVEN** a prompt containing a resource link to a workspace file and one to an external URI
- **WHEN** the prompt reaches the model
- **THEN** the first appears as `@<relative path>` and the second as `<name> (<uri>)`, in order
