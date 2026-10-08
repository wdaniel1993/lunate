## ADDED Requirements

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
