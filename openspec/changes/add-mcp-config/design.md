# Design: MCP config, prefixing, approvals and TUI integration

## The config file

`~/.lunate/mcp.json`, Claude-Desktop-compatible:

```json
{
  "mcpServers": {
    "files": {
      "command": "npx",
      "args": ["-y", "@modelcontextprotocol/server-filesystem", "/tmp"],
      "env": { "K": "V" },
      "cwd": "/optional/working/dir",
      "callTimeoutSeconds": 30
    }
  }
}
```

- Server **name = the object key**; it becomes the tool namespace and the `server__tool` prefix.
- `command` required; `args`/`env` optional; `cwd` optional (passed through as the server process's working directory, as-is); `callTimeoutSeconds` optional (null → the host default).
- Unknown fields inside a server entry are **ignored** (forward compatibility); a malformed entry (missing/blank `command`, wrong JSON types) is an **error entry naming the server and field**, the server is skipped and the session continues. A malformed or unreadable file is one actionable error (path + parse position) and the session continues without configured servers.
- Parsing lives in `Lunate.Protocols` (`McpConfig.Load(path)` → server options + error list; Protocols owns the MCP domain). No new packages.
- Read **at session start only**; `--version`/`--help`/`--discover`/flag parsing never touch the file (the same discipline the extension store follows; proven by a poisoned-file test).

## Composition (harmonized with the extension host)

- The extension host's `ExtensionMcpServers` mapping is generalized into a shared **`McpToolSet`** (Coding, internal): constructed from a list of `McpServerOptions` + the mode's log; `ListToolsAsync` starts each server once, isolates failures, and records a **per-server status** (not started / started + tool count / failed + reason); `DisposeAsync` stops all. `ExtensionMcpServers`' registration→options mapping stays and feeds the same set — one listing/dispose path for config and extension servers.
- Each mode composes configured servers at session start, next to the extension host (interactive/print/ACP; the same three points), lists once before the first turn, and adds the tools to the registry. `--no-extensions` skips the extension host only.
- **Failure surfacing uses the `IUserInteraction` seam** (T-40): a failed server produces a `Warn` notice — live area in the TUI, `lunate:`-prefixed stderr in print, host log in ACP — plus the `McpToolSet` status for `/mcp`. No raw stderr writes from the composition.
- Path seam: `McpConfigPath` on the mode options (tests inject a temp file; production default `~/.lunate/mcp.json`).

## Prefixing and approvals (existing contract, now proven)

- Names stay `server__tool` with the server namespace; exposure defaults to `Direct` (the T-29/T-41 contract; nothing new). Tests assert the names and namespace for config servers.
- Risk stays `Execute`; the existing policy maps it: `ask` → asks, `auto-edit` → asks, `yolo` → runs (guide 786). Proof is end to end: a print run with the default `ask` policy gets an approval-refused error result for an MCP call; with `--yolo` the same call executes. Interactive approvals ride the existing prompt chain (no new approval UI).

## TUI

- **`/mcp`** joins the built-in commands (alphabetical; Tab completion free): renders one dim line per configured server — `files — started, 4 tools`, `files — not started`, `files — failed: <reason>`; no servers configured → `mcp: no servers configured`. Data comes from the `McpToolSet` status (live: a server started by a first listing shows started).
- Server failures at session start appear as notices (above); tool calls render through the existing tool-block path with their `server__tool` name (asserted, not assumed).
- Tool-list-change surfacing beyond the existing callback stays out of scope (seam noted).

## `--no-extensions` (proposed)

A CLI flag accepted in every mode: skips composing the extension host (so extension hooks, renderers, widgets and extension MCP servers are absent); configured `mcp.json` servers stay (they are user-curated, not third-party bundles). The guide promises it (line 791); it lands on the seam this change already touches. If the sign-off prefers, it can be split into its own card.

## Out of scope

- HTTP/SSE transports, resources/prompts/sampling (guide v1 scope), exposure overrides in `mcp.json`, per-server enable/disable toggles, `/mcp` interactive sub-commands (restart etc.).

## Risks

- A hanging `mcp.json` server is bounded by the existing call timeout + dispose paths (T-29); start failures are notices, never session failures.
- The `McpToolSet` refactor touches the extension MCP path shipped in compose-extension-host — its tests must stay green unchanged (regression gate).
