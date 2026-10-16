## MODIFIED Requirements

### Requirement: MCP servers from mcp.json

The CLI SHALL read stdio server configurations from five files, each a `mcpServers` map keyed by server name, each entry carrying `command` (required), `args`, `env`, optional `cwd` and optional `callTimeoutSeconds`; unknown fields SHALL be ignored. The files SHALL be: the user files `~/.lunate/mcp.json` and `~/.agents/mcp.json`, and the worktree's `.lunate/mcp.json`, `.agents/mcp.json` and repo-root `.mcp.json`. Entries SHALL merge by server name with the precedence `.lunate/mcp.json` > `.agents/mcp.json` > `.mcp.json` within each scope and every project file over every user file; each shadowed definition SHALL be logged naming the server and both source files. `command`, `args` and `env` values SHALL support Claude Code-compatible expansion — `${VAR}` to the variable's value, `${VAR:-default}` to the value or the default — computed when the file loads; a variable that is unset and has no default SHALL leave the literal text in place and log a warning naming the server and the variable, surfaced next to the server in `/mcp`. Because a project file runs repository-provided commands, each of the three project files SHALL require a one-time trust decision per repository identity, re-prompted when the file changes (the project-extension discipline), and each server defined by a project file SHALL additionally require a one-time approval per definition before it can start — keyed by the repository identity and a canonical hash of its raw definition as written, re-prompted when that definition changes. Both hashes SHALL be computed over the raw definitions as written — before any `${VAR}` expansion, canonically serialized — so they are machine-independent and never capture expanded values or secrets. Non-interactive modes SHALL refuse an untrusted project file or an unapproved project server with a logged hint naming the approval paths — an interactive session or the `lunate trust` command — and continue with the rest. The files SHALL be read when a session starts — never during CLI startup; `--version`, `--help`, `--discover` and flag parsing SHALL NOT read them; the `lunate trust` command reads the project files only to evaluate and record trust, never to start servers. Only servers listed in the files SHALL ever start. Each configured server SHALL follow the existing lazy-server contract: nothing starts before its first listing, tools are named `server__tool` with the server's namespace and risk `Execute`, and a server that fails to start or list SHALL produce an actionable notice naming the server while the session continues without its tools. A malformed entry SHALL be reported naming the server and field and the server skipped; a malformed or unreadable file SHALL be reported with its path and the session SHALL continue without its servers.

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

#### Scenario: A standard-location server is read and wins over the root file

- **GIVEN** a worktree whose `.agents/mcp.json` and root `.mcp.json` define the same server name, both trusted and approved
- **WHEN** a session starts
- **THEN** the `.agents/mcp.json` definition is used and a collision line names the server and both files

#### Scenario: Expansion substitutes values and defaults

- **GIVEN** a trusted project `mcp.json` whose `command`, `args` and `env` reference `${VAR}` and `${VAR:-default}`
- **WHEN** a session starts with `VAR` set and the defaulted variable unset
- **THEN** the server starts with the expanded values

#### Scenario: An unset variable without a default warns and stays literal

- **GIVEN** a trusted project `mcp.json` referencing an unset `${VAR}` with no default
- **WHEN a session starts**
- **THEN** a warning names the server and the variable, and the literal `${VAR}` text is used

#### Scenario: A project server needs its own approval once

- **GIVEN** a trusted project file with a server definition and no per-server approval
- **WHEN** an interactive session starts and the per-server prompt is approved
- **THEN** the approval is recorded for that definition in this repository, a later session does not ask again, another repository with the same definition still asks, and a changed definition re-prompts

#### Scenario: An unapproved project server is refused non-interactively

- **GIVEN** a trusted project file whose server has no per-server approval
- **WHEN** a print-mode session starts
- **THEN** the server does not start, a log line names the approval paths, and the rest of the session works
