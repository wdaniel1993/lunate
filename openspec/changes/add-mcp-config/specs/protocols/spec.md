## ADDED Requirements

### Requirement: MCP servers from mcp.json

The CLI SHALL read stdio server configurations from `~/.lunate/mcp.json` — a `mcpServers` map keyed by server name, each entry carrying `command` (required), `args`, `env`, optional `cwd` and optional `callTimeoutSeconds`; unknown fields SHALL be ignored. The file SHALL be read when a session starts — never during CLI startup; `--version`, `--help`, `--discover` and flag parsing SHALL NOT read it. Only servers listed in the file SHALL ever start. Each configured server SHALL follow the existing lazy-server contract: nothing starts before its first listing, tools are named `server__tool` with the server's namespace and risk `Execute`, and a server that fails to start or list SHALL produce an actionable notice naming the server while the session continues without its tools. A malformed entry SHALL be reported naming the server and field and the server skipped; a malformed or unreadable file SHALL be reported with its path and the session SHALL continue without configured servers.

#### Scenario: A configured server works in a session

- **GIVEN** a `mcp.json` configuring a working stdio server
- **WHEN** an interactive session starts and the model calls one of its tools
- **THEN** the tool is listed as `server__tool`, the call round-trips through the approval policy and the result renders in the session

#### Scenario: Nothing starts for non-session commands

- **GIVEN** a `mcp.json` whose server would write a marker when started
- **WHEN** `lunate --version`, `--help` or `--discover` runs
- **THEN** the file is not read and no server process starts

#### Scenario: A broken entry is actionable and isolated

- **GIVEN** a `mcp.json` with one valid and one malformed entry
- **WHEN** a session starts
- **THEN** the malformed entry is reported naming the server and field, the valid server still works, and the session is unaffected

#### Scenario: A failing server never breaks the session

- **GIVEN** a configured server that cannot start
- **WHEN** a session starts
- **THEN** a notice names the server, the session continues without its tools, and other tools behave normally

#### Scenario: MCP calls follow the approval policy

- **GIVEN** a configured working server and the default `ask` policy
- **WHEN** the model calls an MCP tool in print mode
- **THEN** the call is refused with an approval error result; with `--yolo` the same call executes
