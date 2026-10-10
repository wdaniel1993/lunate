## ADDED Requirements

### Requirement: ACP server over stdio

`lunate --acp` SHALL speak the Agent Client Protocol over stdio so editors drive Lunate as their agent, behind an `IAcpServer` seam (`RunAsync(input, output, createHarness, ct)`) that keeps a later SDK switch cheap. The server SHALL implement `initialize` (protocol version 1, honest capabilities, negotiate down, log unsupported), session creation (a fresh harness per ACP session, the session cwd as its workspace), `session/prompt` (one run; text content blocks; streaming `session/update` notifications mapped from `AgentEvent`s per the guide's event table; the response carries the stop reason) and `session/cancel` (cancels the in-flight run; the prompt response reports the cancellation; no updates follow it). Nothing but protocol frames may be written to stdout; logs go to stderr. The mode SHALL be mutually exclusive with print mode and the TUI (usage error, exit 2). The server SHALL be tested with an in-process client over a pipe pair.

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
