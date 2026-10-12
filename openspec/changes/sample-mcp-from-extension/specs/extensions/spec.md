## MODIFIED Requirements

### Requirement: MCP servers from extensions

The contract assembly SHALL define a JSON-serializable MCP server registration — the `mcp.json` shape (name, command, args, env, working directory, call timeout), an exposure (default `Direct`) and per-tool exposure overrides by the server's own tool name — and `IExtensionContext` SHALL gain `RegisterMcpServer` for it. Registration SHALL perform no I/O and start nothing; the first tool listing starts the process exactly once (the existing lazy-server behaviour). The loader SHALL collect registrations at creation time and expose them; the first registration per server name SHALL win, and a duplicate SHALL be refused and logged naming both extensions. A registration whose server name matches a configured `mcp.json` server SHALL be skipped before listing in every mode — the configured server wins — and the collision SHALL be logged naming both sources and surfaced by `/mcp`. Each server SHALL give its tools a namespace named after the server, and the configured exposure and overrides SHALL gate the wrapped tools exactly as for core tools; an override naming no listed tool SHALL be logged. A server that fails to start or list SHALL produce an actionable error naming the server, logged by the host, and the session SHALL continue without its tools. A started server whose process exits SHALL flip to a failed status with the reason `the server process exited` when next observed — a tool call or `/mcp` — and its tools SHALL read as unavailable. The observation points exist where the session surfaces MCP status (interactive mode); print and ACP sessions keep the existing call-error path.

#### Scenario: Registration is registration only

- **GIVEN** an extension registering a server whose command cannot exist
- **WHEN** the extension is loaded
- **THEN** nothing is started and nothing is logged at registration; only first use reports the failure

#### Scenario: Duplicate server names are refused and named

- **GIVEN** two extensions registering a server with the same name
- **WHEN** the second registration is collected
- **THEN** it is refused and the log names both extensions, and the first registration's server remains the one that starts

#### Scenario: Tools arrive namespaced and exposure-gated

- **GIVEN** a registered server with an exposure override marking one tool `Hidden`
- **WHEN** its tools are listed and declarations are taken
- **THEN** every tool carries the server's namespace, the hidden tool is absent from the model's declarations, and the others follow their exposure

#### Scenario: An unknown override is logged, never silently ignored

- **GIVEN** a registration whose overrides name a tool the server does not list
- **WHEN** the tools are listed
- **THEN** the host log names the unknown override and the rest proceeds normally

#### Scenario: A failing server never breaks the session

- **GIVEN** a registered server that fails to start
- **WHEN** its tools are first requested
- **THEN** the host logs an actionable error naming the server and the session continues without its tools

#### Scenario: A configured server wins a name collision

- **GIVEN** a configured server and an extension registration with the same name
- **WHEN** the session composes
- **THEN** the configured server starts and lists its tools, the registration is skipped before listing, and the collision is logged naming both sources and surfaced by `/mcp`

#### Scenario: A crashed server flips to failed

- **GIVEN** a started server whose process has exited
- **WHEN** the server is next observed (a failed tool call or `/mcp` in interactive mode)
- **THEN** its status reads failed with `the server process exited` and its tools read as unavailable
