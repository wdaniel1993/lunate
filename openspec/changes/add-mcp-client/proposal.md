# add-mcp-client

## Why

Card T-29 (deps T-08): the MCP client on the **official MCP C# SDK** (approved by the maintainer 2026-10-08), per the guide's MCP section: servers start on first use; their tools are wrapped as `ITool`s with risk `Execute` so the approval policy applies; names are prefixed `server__tool`; refresh on tool-list changes; cancel with the turn; timeout hanging servers; a crashing server becomes a tool error, never a Lunate crash; stdio only in v1; resources/prompts/sampling out of scope.

Done-gate: tests against `tests/tools/TestMcpServer` (two normal tools, one slow tool, one crashing tool).

## What Changes

- **`Lunate.Protocols` gains the MCP client**: `McpServerHost` (per-server lifecycle: lazy start on first use, stdio transport, dispose stops the process) and `McpToolAdapter : ITool` (name `server__tool`, MCP description + JSON schema, risk `Execute`, `CallToolAsync` mapping, structured content passthrough, timeout/error translation, cancellation via `OperationCanceledException`).
- **Tool-list changes**: the SDK's list-changed notification triggers a refresh; the host surfaces the updated tool set to a callback.
- **`tests/tools/TestMcpServer`**: tiny stdio MCP server (official SDK server side) with `echo` + `add` (normal), `slow` (cancellable sleep), `boom` (always throws), and `spawn_tool` (registers a new tool + notifies). Writes a start marker (path via env) so tests can prove lazy start.
- Config parsing (`~/.lunate/mcp.json`) and approval/UI wiring stay in T-30; this change takes an options object (name, command, args, env, cwd).
- Spec delta: new `protocols` capability.

## Impact

- `Lunate.Protocols` (first real implementation; PublicAPI entries), `tests/Lunate.Protocols.Tests`, `tests/tools/TestMcpServer` (new project, added to the solution), layering registration for the new project if the checker needs it. One new package (MCP SDK — approved) in Protocols + TestMcpServer.
