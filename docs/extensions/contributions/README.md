# Contribution surfaces

Each page below covers one surface an extension can contribute: the API, the constraints the host
enforces, and a short example taken from a reference extension or fixture.

All registration happens in `IExtensionFactory.Create`, through the `IExtensionContext` argument.
Registration is registration only: `Create` must not start processes, sockets or timers. Long-lived
resources belong in a background service or start on first use.

| Surface | Register with | Page | Reference |
| --- | --- | --- | --- |
| Hook handlers | `context.Register(handler)` | [hooks.md](hooks.md) | `permission-gate`, `lsp-diagnostics` |
| Tools | `context.RegisterTool(registration)` | [tools.md](tools.md) | `ui-showcase` |
| Named services | `context.RegisterService(name, service)` | [services.md](services.md) | `memory-provider`, `model-router` |
| File-change subscriptions | `context.SubscribeFileChanged(handler, pattern)` | [file-changes.md](file-changes.md) | loader fixture `HelloExtension` |
| MCP servers | `context.RegisterMcpServer(registration)` | [mcp-servers.md](mcp-servers.md) | `mcp-from-extension` |
| Model providers | manifest `modelProviders`, or `context.RegisterModelProvider(provider)` | [model-providers.md](model-providers.md) | `model-router` |
| Model middleware | `context.RegisterModelMiddleware(middleware)` | [model-middleware.md](model-middleware.md) | `model-router` |
| Virtual models | `context.RegisterVirtualModel(model)` | [virtual-models.md](virtual-models.md) | `model-router` |
| Skill sources | `context.RegisterSkillSource(source)` | [skill-sources.md](skill-sources.md) | `SkillSourceExtension` fixture |
| Tool-result renderers | `context.RegisterToolResultRenderer(renderer)` | [tool-result-renderers.md](tool-result-renderers.md) | `ui-showcase` |
| Status widgets | `context.RegisterStatusWidget(widget)` | [status-widgets.md](status-widgets.md) | `ui-showcase` |
| Settings and secrets | `context.Settings`, `context.Secrets` | [settings-and-secrets.md](settings-and-secrets.md) | `permission-gate`, `model-router` |
| Background services | `context.RegisterService(name, service)` | [background-services.md](background-services.md) | `lsp-diagnostics`, `memory-provider` |

## Commands and keybindings

Extension slash commands and keybindings are planned and not available in this version:
`IExtensionContext` has no registration method for either, and a manifest `commands` entry is shown
by `/extensions` as `commands 0 (N declared)`.

## Caps and duplicate handling

| Surface | Cap | Duplicate |
| --- | --- | --- |
| Tools | 32 per extension | A model-facing name already registered by another extension refuses the load. |
| Model providers | 8 per extension (declared plus programmatic) | A duplicate provider id or served model id refuses the load; a catalog model id cannot be shadowed. |
| Model middleware | 16 per extension | No id; every registration is kept. |
| Virtual models | 8 per extension | A duplicate namespaced id refuses the load. |
| MCP servers | No cap | The first registration per server name wins; the later one is refused and logged. |
| Skill sources | No cap | The last registration per source id wins, with a warning. |
| Tool-result renderers | No cap | The last renderer per tool name wins, with a warning. |
| Status widgets | No cap | Every registration contributes, in registration order. |
| Named services | No cap | A duplicate name refuses the load; `core/` names are reserved. |

“Refuses the load” means the extension ends up in the `failed` state in `/extensions`; the message
names the extension and the field. “Wins” means the session continues with one contribution and
the conflict is logged.
