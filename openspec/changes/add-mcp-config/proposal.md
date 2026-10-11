# Proposal: MCP config, prefixing, approvals and TUI integration

## Why

T-29 landed the MCP client (lazy servers, wrapping, cancel, list changes) and T-41 wired extension-declared servers; T-30 was explicitly deferred and owns the remaining user-facing half: **`~/.lunate/mcp.json` parsing, tool-name prefixing in a real session, approval wiring, and TUI integration** (guide card T-30; MCP section lines 515–530 and the security section line 786/791 are binding). Done when one real MCP server works in the TUI.

## What changes

- **`~/.lunate/mcp.json`**: the Claude-Desktop-compatible `mcpServers` map (per server: `command`, `args`, `env`; optional `cwd`, `callTimeoutSeconds`). Read at session start (never at CLI startup — the budgets hold), path is a test seam. Malformed files and malformed entries produce actionable errors and the session continues without the affected servers.
- **Session-start composition**: configured servers join the same composition the extension host uses — one harmonized listing path for config and extension servers (the `ExtensionMcpServers` mapping is generalized into a shared tool set), tools listed once at session start, failures surfaced as notices through the existing `IUserInteraction` seam (live area / stderr / host log per mode).
- **Prefixing**: config servers follow the existing contract — tools named `server__tool`, namespaced after the server key, exposure `Direct`.
- **Approvals**: MCP tools carry risk `Execute`, so the existing policy applies (`ask` and `auto-edit` ask for every MCP call; `yolo` runs) — proven end to end with the real policy, not assumed.
- **TUI**: a `/mcp` command lists configured servers with status (not started / started with tool count / failed with reason); server start and list failures appear as notices when the session starts; tool calls render with their `server__tool` names.
- **`--no-extensions`** (the guide's promise, line 791): starts without composing the extension host (extension MCP included); configured `mcp.json` servers are user-curated and stay. *Flagged for sign-off.*

## Done when

A real MCP server configured in a temp `mcp.json` runs in an interactive session: its namespaced tools are listed, a call round-trips and renders, `/mcp` shows its status, a broken server notifies and never breaks the session, and `lunate --version` provably never reads `mcp.json`.
