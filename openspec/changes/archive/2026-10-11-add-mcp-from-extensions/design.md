# Design: MCP servers from extensions (T-41)

## Structure

- `src/Lunate.Extensibility.Abstractions/Mcp/McpServerRegistration.cs` (new): `McpServerRegistration(string Name, McpServerConfig Config, ToolExposure Exposure = ToolExposure.Direct, IReadOnlyDictionary<string, ToolExposure>? ExposureOverrides = null)`; `McpServerConfig(string Command, IReadOnlyList<string> Arguments, IReadOnlyDictionary<string, string>? Environment = null, string? WorkingDirectory = null, TimeSpan? CallTimeout = null)` — the `mcp.json` shape; `enum ToolExposure { Direct, ModelOnly, Programmatic, Deferred, Hidden }` — contract-owned, mirroring `Lunate.Agent.ToolExposure` 1:1 (the contract assembly references no Lunate project). All DTOs round-trip through `System.Text.Json`.
- `IExtensionContext.RegisterMcpServer(McpServerRegistration registration)` (new member).
- `src/Lunate.Extensibility/ExtensionContext.cs` + the loader: an `McpServerRegistry` alongside the renderer/widget registries — collected at creation time, exposed on the loader (`McpServers`), registration performs no I/O and starts nothing. First registration per name (ordinal) wins; a duplicate is refused and logged naming both extension ids.
- `src/Lunate.Protocols/`: `McpServerOptions` gains optional `Exposure` (default `Direct`), `ExposureOverrides`, and `NamespaceName` (default: the server name); `McpToolAdapter` carries the resolved exposure and the namespace (`Namespace(Name = namespace name)`; description/instructions stay null) on the wrapped `ITool`s. Overrides match the server's own tool name (ordinal); an override naming no listed tool is logged through the host log. Risk stays `Execute` (approvals unchanged).
- `src/Lunate.Coding/ExtensionMcpServers.cs` (new, internal): maps `McpServerRegistration` → `McpServerOptions` + starts hosts lazily (first use) — the single mapping used by the end-to-end test and, later, by the compose-the-extension-host work. A server that fails to start or list logs an actionable error naming the server and the session continues without its tools (the existing Protocols failure behavior, stated at this seam).

## Pins

- **Registration is registration only**: `RegisterMcpServer` spawns nothing; the first tool listing starts the process exactly once (the T-29 laziness, unchanged). The end-to-end test asserts no process before first use.
- **Namespace**: one namespace per server, `Name` = the server name (the registered name, which also prefixes tool names `server__tool`). No description/instructions from config in v1.
- **Exposure**: server-level default + per-tool overrides by the server's own tool name; unknown override names are logged (typo protection), never silently ignored. Exposure maps 1:1 to `Lunate.Agent.ToolExposure`; `Declarations` therefore gates `Hidden`/`Programmatic`/`Deferred` exactly as for core tools.
- **Duplicates**: first registration per server name wins; later ones are refused + logged naming both extensions — never a silent shadow.
- **Failures**: start/list failure at first use → logged, session continues without the server's tools (matches the hook failure policy spirit: never crash the run).
- **No new packages**; the MCP SDK stays as pinned in T-29.

## Tests

- Contract: `McpServerRegistration`/`McpServerConfig` round-trip through STJ.
- Loader: registration collected; nothing started (a config whose command cannot exist starts nothing and logs nothing at registration); duplicate name refused + logged naming both ids.
- Protocols: wrapped tools carry namespace + exposure; overrides applied; unknown override logged; existing MCP suites green (regression).
- End to end (the done-when): a fixture extension (new `tests/TestExtensions/` project following `HelloExtension`) registers the real `tests/tools/TestMcpServer` binary with a `Hidden` override for one tool; load through `ExtensionTestHost`, map via `ExtensionMcpServers`, list tools, assert: namespaced names, `Hidden` tool absent from `Declarations`, and one tool call round-trips through the harness with its result.

## Deviations

- **API version bumped to 1.5.0** (not named in the draft): the repo convention is one minor bump per contract extension (`ExtensionApi.Current`), and without it a 1.4.0 host would load an extension that calls `RegisterMcpServer` and fail at runtime. `ExtensionApi` and `ExtensionApiTests` updated.
- **Unknown-override logging uses an optional `Action<string>? log` parameter on `McpServerHost`**: `Lunate.Protocols` has no logging abstraction and must not reference `Lunate.Extensibility`; the coding mapping passes the extension log (warn level) through that parameter.
- **The fixture registers settings-driven command/args/marker** (settings written by the test into the host store before load); with no settings it registers the default command that cannot exist, which is what the registration-only tests exercise.

## Seams

- Production composition of the loader remains the separate compose-the-extension-host card (`t_51bb695f`); this change deliberately lands the mapping in `Lunate.Coding` so that card wires it without rework.
- MCP tool renderers by name (T-40) already resolve for these tools, including before a resumed session registers them.
