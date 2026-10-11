## MODIFIED Requirements

### Requirement: Extension loading

The host SHALL discover extensions in `~/.lunate/extensions/` (global) and `.lunate/extensions/` (project), reading manifests only — assemblies SHALL load lazily on first use into a collectible `AssemblyLoadContext` that shares the abstractions, `Microsoft.Extensions.AI.Abstractions` and `System.Text.Json` with the default context and isolates every other dependency. Unload SHALL be idempotent; a later load SHALL work; state SHALL not survive. Duplicate ids across scopes SHALL be an error naming both paths. Missing dependencies SHALL yield an actionable error. The CLI SHALL compose the host when a session starts — the host's first use — for interactive, print and ACP sessions; `--version`, `--help` and non-session commands SHALL NOT read the extension store. `--no-extensions` SHALL start every mode without composing the host (extension hooks, renderers, widgets and extension MCP servers absent; configured `mcp.json` servers unaffected). A discovery or load failure SHALL be logged through the host log and SHALL NOT abort the session.

#### Scenario: Manifests are read without loading assemblies

- **GIVEN** an installed extension
- **WHEN** discovery runs
- **THEN** its manifest is read and its assembly is not loaded

#### Scenario: A loaded extension unloads and reloads

- **GIVEN** a loaded extension
- **WHEN** it is unloaded and loaded again
- **THEN** both operations succeed and the second instance is fresh

#### Scenario: A missing dependency fails actionably

- **GIVEN** an extension whose entry assembly requires an assembly that is not present
- **WHEN** it is loaded
- **THEN** loading fails with an error naming the missing assembly and the extension

#### Scenario: A session composes the host at start

- **GIVEN** an extension installed in `~/.lunate/extensions/`
- **WHEN** an interactive, print or ACP session starts
- **THEN** the extension is discovered and loaded as the session starts, and its hook observes the session's tool calls

#### Scenario: Non-session commands never touch the store

- **GIVEN** any installed extension
- **WHEN** a non-session command (`lunate --version`, `lunate --help`, `lunate --discover`) runs
- **THEN** no manifest is read and no assembly is loaded

#### Scenario: One broken extension does not stop the session

- **GIVEN** an installed extension whose load fails
- **WHEN** a session starts
- **THEN** the failure is logged through the host log and the session runs without that extension

#### Scenario: --no-extensions starts without the host

- **GIVEN** an installed extension and a configured `mcp.json` server
- **WHEN** a session starts with `--no-extensions`
- **THEN** the extension store is not read, its hook never runs, and the configured MCP server still works
