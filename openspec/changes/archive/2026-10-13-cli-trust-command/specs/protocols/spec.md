## MODIFIED Requirements

### Requirement: MCP servers from mcp.json

The CLI SHALL read stdio server configurations from `~/.lunate/mcp.json` — a `mcpServers` map keyed by server name, each entry carrying `command` (required), `args`, `env`, optional `cwd` and optional `callTimeoutSeconds`; unknown fields SHALL be ignored. It SHALL also read `.lunate/mcp.json` inside the worktree; entries SHALL merge by server name with the project file winning. Because a project file runs repository-provided commands, it SHALL require a one-time trust decision per repository identity, re-prompted when the file changes (the project-extension discipline); the trust hash SHALL be computed over the servers' raw definitions as written — before any `${VAR}` expansion, canonically serialized — so it is machine-independent and never captures expanded values or secrets. Non-interactive modes SHALL refuse an untrusted project file with a logged hint naming the approval paths — an interactive session or the `lunate trust` command — and continue with the global servers only. The files SHALL be read when a session starts — never during CLI startup; `--version`, `--help`, `--discover` and flag parsing SHALL NOT read them; the `lunate trust` command reads the project file only to evaluate and record its trust, never to start servers. Only servers listed in the files SHALL ever start. Each configured server SHALL follow the existing lazy-server contract: nothing starts before its first listing, tools are named `server__tool` with the server's namespace and risk `Execute`, and a server that fails to start or list SHALL produce an actionable notice naming the server while the session continues without its tools. A malformed entry SHALL be reported naming the server and field and the server skipped; a malformed or unreadable file SHALL be reported with its path and the session SHALL continue without its servers.

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

#### Scenario: An untrusted project file prompts once

- **GIVEN** a repository with a project `mcp.json` and no trust record
- **WHEN** an interactive session starts and the prompt is approved
- **THEN** the decision is recorded and a later session does not prompt again; a changed file re-prompts

#### Scenario: A denied project file never starts its servers

- **GIVEN** a project `mcp.json` whose trust is denied
- **WHEN** a session starts
- **THEN** the project servers do not start (a log line says how to approve), and global servers still work

#### Scenario: A project entry overrides a same-named global entry

- **GIVEN** a server name configured in both the global and the trusted project file
- **WHEN** a session starts
- **THEN** the project file's definition is the one used

#### Scenario: MCP calls follow the approval policy

- **GIVEN** a configured working server and the default `ask` policy
- **WHEN** the model calls an MCP tool in print mode
- **THEN** the call is refused with an approval error result; with `--yolo` the same call executes

#### Scenario: The trust command starts nothing

- **GIVEN** a project `mcp.json` whose server would write a marker when started
- **WHEN** `lunate trust --yes` runs
- **THEN** the trust is recorded and no server process starts
