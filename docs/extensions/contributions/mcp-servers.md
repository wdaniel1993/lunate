# MCP servers

An extension can contribute an MCP server with `context.RegisterMcpServer`. The registration uses
the `mcp.json` shape and adds exposure controls for the whole server and for individual tools. The
manifest has no MCP field: this surface exists only through code.

## The API

```csharp
context.RegisterMcpServer(
    new McpServerRegistration(
        "fake",
        new McpServerConfig("dotnet", ["exec", serverDll], environment),
        ToolExposure.Direct,
        new Dictionary<string, ToolExposure> { ["secret"] = ToolExposure.Hidden }
    )
);
```

| Type | Members |
| --- | --- |
| `McpServerRegistration` | `Name`, `Config`, `Exposure` (default `Direct`), `ExposureOverrides` |
| `McpServerConfig` | `Command`, `Arguments`, optional `Environment`, `WorkingDirectory`, `CallTimeout` |

`McpServerRegistration` uses the contract enum `Lunate.Extensibility.Abstractions.ToolExposure`;
extension tools use `Lunate.Agent.ToolExposure`. The member names are the same: `Direct`,
`ModelOnly`, `Programmatic`, `Deferred`, `Hidden`.

## Constraints

- **Registration only.** Nothing starts at registration. The process starts on the first tool
  listing, exactly once, and the listed tools are cached for later listings.
- **Name.** The server name is required and is the tool namespace: every tool arrives as
  `<server>__<tool>`. The first registration per name wins; a later one is refused and logged
  naming both extensions. A configured `mcp.json` server with the same name wins over an extension
  registration, and `/mcp` shows the extension server as shadowed.
- **Exposure.** The server-level exposure applies to every tool; `ExposureOverrides` replaces it
  per tool name. A `Hidden` tool is neither declared to the model nor callable through
  `ToolContext.ExecuteToolAsync`.
- **Failure isolation.** A missing binary or a failed start logs the server and its extension and
  the session continues without its tools. A started process that exits flips to failed on the
  next observation; one log line, and one notice in the interactive session.
- **Approval.** MCP tools have risk `ToolRisk.Execute`, so their calls go through the same
  approval policy as core tools.
- **No manifest declaration.** There is no `servers` manifest field and no cross-check.

## Example

`mcp-from-extension` registers one server named `fake` that runs the bundled fake server next to
the extension assembly, with `secret` hidden:

```csharp
McpSettings settings = McpSettings.From(context.Settings);
string serverDll = Path.Combine(
    Path.GetDirectoryName(typeof(McpFromExtensionExtensionFactory).Assembly.Location)!,
    "FakeMcpServer.dll"
);
IReadOnlyDictionary<string, string>? environment = settings.MarkerPath
    is { Length: > 0 } marker
    ? new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["LUNATE_FAKE_MCP_MARKER"] = marker,
    }
    : null;
context.RegisterMcpServer(
    new McpServerRegistration(
        "fake",
        new McpServerConfig("dotnet", ["exec", serverDll], environment),
        ToolExposure.Direct,
        new Dictionary<string, ToolExposure> { ["secret"] = ToolExposure.Hidden }
    )
);
```

Full file:
[`McpFromExtensionExtensionFactory.cs`](../../../samples/extensions/mcp-from-extension/McpFromExtensionExtension/McpFromExtensionExtensionFactory.cs).
The sample's tests prove the lazy start, the `fake__tool` names, the exposure gate and crash
status.

## See also

- MCP tools in the core: [`docs/spec/extensibility.md`](../../spec/extensibility.md#services-for-long-lived-integrations)
- Configuring MCP servers outside extensions: [`README.md`](../../../README.md#mcp-servers)
- Out-of-process extensions: [remote extensions](../../extensions-remote.md)
