# Proposal: MCP servers from extensions (T-41)

## Why

The MCP client (T-29) wraps stdio servers as `server__tool` tools, but nothing lets an extension contribute a server: the contract has no `RegisterMcpServer`, the loader collects nothing, and wrapped tools carry no namespace or exposure — so an extension's MCP tools cannot be grouped for discovery or gated like every other tool. The plan's model (`docs/spec/extensibility.md`): `RegisterMcpServer(name, config)` with the `mcp.json` shape plus exposure per server/tool; each server gets a namespace.

## What changes

- **Contract**: `IExtensionContext.RegisterMcpServer(McpServerRegistration)` — the server config (command, args, env, cwd, call timeout), a contract-owned exposure enum mirroring the tool model, and per-tool exposure overrides; JSON-serializable, registration-time only.
- **Loader**: an MCP-server registry collected from loaded extensions (first registration per name wins; a duplicate is refused and logged naming both extensions).
- **Protocols**: the wrapped tools gain the namespace (server name) and the configured exposure; exposure overrides resolve by the server's own tool name, and an override naming no listed tool is logged.
- **Coding**: the mapping from registration to `McpServerOptions`/namespace/exposure lives where production composition will use it, so the compose-the-host card reuses it.
- **End to end**: a fixture extension registers the real `TestMcpServer`; its tools arrive namespaced, exposure-gated (a `Hidden` override keeps a tool out of the model's declarations) and callable through the harness.

## Done when

The end-to-end test passes with the actual `TestMcpServer` binary registered by a loaded extension, registration spawns nothing, duplicates and unknown overrides are logged, and `scripts/verify.sh` is green.
