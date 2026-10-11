## MODIFIED Requirements

### Requirement: MCP client with lazy servers

The MCP client SHALL connect stdio servers configured by options (name, command, args, env, cwd) and SHALL start a server process only on first use — never at construction or startup. Server tools SHALL be wrapped as `ITool`s named `server__tool` with the MCP description and input schema, carrying risk `Execute` so the approval policy applies, a namespace named after the server, and the exposure configured for the server or the individual tool (default `Direct`); overrides SHALL match the server's own tool name and an override naming no listed tool SHALL be logged. Disposing the host SHALL stop the process idempotently and leave no orphans; setup and process failures SHALL produce clear, actionable errors naming the server.

#### Scenario: Nothing starts before first use

- **GIVEN** a configured server that marks its start
- **WHEN** the host is constructed but no tool is requested
- **THEN** no process has started; the first tool listing starts it exactly once

#### Scenario: Tools arrive wrapped and prefix-namespaced

- **GIVEN** a started server with known tools
- **WHEN** tools are listed
- **THEN** each tool appears as `server__tool` with its schema and risk `Execute`

#### Scenario: Exposure and namespace follow the options

- **GIVEN** a started server configured with a per-tool exposure override
- **WHEN** tools are listed
- **THEN** each tool carries the server's namespace and the configured exposure, with the override applied to its named tool

#### Scenario: Dispose leaves no process behind

- **GIVEN** a started server
- **WHEN** the host is disposed (twice, for good measure)
- **THEN** the child process is gone and later calls fail cleanly
