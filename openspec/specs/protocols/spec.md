# protocols Specification

## Purpose
Lunate speaks external agent tool protocols from `Lunate.Protocols`. v1 is an MCP client: stdio servers start lazily, their tools become Lunate tools (`server__tool`, risk `Execute` so the approval policy applies), tool-list changes refresh, and calls honor cancellation, timeouts and server failures without ever crashing a run. Remote transports and ACP follow the same split: protocol I/O here, semantics in the core.

## Requirements

### Requirement: MCP client with lazy servers
The MCP client SHALL connect stdio servers configured by options (name, command, args, env, cwd) and SHALL start a server process only on first use — never at construction or startup. Server tools SHALL be wrapped as `ITool`s named `server__tool` with the MCP description and input schema, carrying risk `Execute` so the approval policy applies. Disposing the host SHALL stop the process idempotently and leave no orphans; setup and process failures SHALL produce clear, actionable errors naming the server.

#### Scenario: Nothing starts before first use
- **GIVEN** a configured server that marks its start
- **WHEN** the host is constructed but no tool is requested
- **THEN** no process has started; the first tool listing starts it exactly once

#### Scenario: Tools arrive wrapped and prefix-namespaced
- **GIVEN** a started server with known tools
- **WHEN** tools are listed
- **THEN** each tool appears as `server__tool` with its schema and risk `Execute`

#### Scenario: Dispose leaves no process behind
- **GIVEN** a started server
- **WHEN** the host is disposed (twice, for good measure)
- **THEN** the child process is gone and later calls fail cleanly

### Requirement: MCP calls honor cancellation, timeouts and failures
Tool calls SHALL propagate the turn's cancellation as `OperationCanceledException`; a call exceeding the configured timeout SHALL return an error result stating the timeout; a crashing tool or dead server SHALL return error results naming the server — a broken server SHALL never crash Lunate, and the client SHALL remain usable afterwards. Tool-list-change notifications from the server SHALL refresh the wrapped tool set and surface it to the host's callback. The client SHALL negotiate a protocol revision whose tool-list changes are broadcast (pinned to 2025-11-25 with SDK 2.2.0).

#### Scenario: A slow call is cancelled with the turn
- **GIVEN** a started server with a slow tool
- **WHEN** the call's token is cancelled during execution
- **THEN** `OperationCanceledException` is raised and the server observes the cancellation

#### Scenario: A hanging call times out as a tool error
- **GIVEN** a short call timeout and a slow tool
- **WHEN** the call runs longer than the timeout
- **THEN** an error result explains the timeout — no exception escapes

#### Scenario: Crashing and dying servers become tool errors
- **GIVEN** a server whose tool throws, or a server process killed mid-session
- **WHEN** tools are called
- **THEN** each failure is an error result that names the server, and subsequent calls still behave cleanly

#### Scenario: Tool list changes refresh the set
- **GIVEN** a started server
- **WHEN** the server registers a new tool and notifies a list change
- **THEN** the host's callback receives the updated wrapped tool set including the new tool

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

### Requirement: ACP approvals, editor file system and resource links

In ACP mode, tool approvals SHALL compose the configured policy with the client's `session/request_permission`: calls the policy already allows SHALL run without prompting; everything else SHALL ask the editor with allow-once, allow-always and reject options — allow-once executes, allow-always executes and is remembered per tool for the session, reject declines as a tool error, and a cancel or request error resolves as a decline. Every client round trip SHALL be bounded: a file request that outlives its timeout SHALL fail as an I/O error, and a permission request that outlives its timeout SHALL decline (both logged) — a stalled editor degrades, it does not hang. A missing file SHALL surface as a not-found failure and other client failures as I/O errors — never masked into one another. Commands SHALL prompt under every policy. When the client advertises the file-system capability, the read, write and edit tools SHALL route file access through the client's `fs/read_text_file`/`fs/write_text_file` so editor buffers are respected (the client owns byte-order marks); without the capability they SHALL use the local disk. Prompt `ResourceLinkContent` blocks SHALL map to model-readable text: a `file://` URI inside the workspace becomes `@<relative path>`, anything else becomes `<name> (<uri>)`, in prompt order.

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

#### Scenario: A stalled file round trip fails as an error

- **GIVEN** a client that never answers a file request
- **WHEN** a file tool runs
- **THEN** it fails as an I/O error after the timeout instead of hanging

#### Scenario: A stalled permission request declines

- **GIVEN** a permission request the client never answers
- **WHEN** its timeout passes
- **THEN** the call is declined (logged) and the run continues with the decline

#### Scenario: Missing files are not masked

- **GIVEN** a client whose read reports the file missing
- **WHEN** a file tool runs
- **THEN** the result says the file was not found, while other client failures surface as I/O errors
