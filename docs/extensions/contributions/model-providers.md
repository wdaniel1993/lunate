# Model providers

A model provider makes new model ids addressable. There are two routes:

- a **declared** provider in the manifest's `modelProviders` array, instantiated lazily as an
  OpenAI-compatible client;
- a **programmatic** provider registered with `context.RegisterModelProvider`, whose factory
  returns an `IChatClient`.

Both appear in the same registry with the same naming and collision rules.

## The API

Declared providers use the descriptor from [manifest.md](../manifest.md#modelproviders). The
programmatic route takes a provider record:

```csharp
public sealed record ExtensionModelProvider(
    string Id,
    IReadOnlyList<string> ModelIds,
    Func<IChatClient> Create
);
```

`Create` is called lazily, at most once per served model id, at first use. The call itself is the
provider's first use, so it may start resources — unlike `IExtensionFactory.Create`.

## Naming and resolution

- The local provider id is `[a-z0-9-]+`. The registry publishes it as
  `ext/<extension-id>/<local-id>`.
- The served `ModelIds` are addressable exactly as written; they are not rewritten. Choosing a
  served id is how a run reaches the provider.
- A served model id may not duplicate another extension's model id or a catalog model id. An
  extension can never shadow a catalog model.
- Resolution order for any model id is: virtual model, then programmatic provider, then declared
  provider, then the session catalog, then a teaching error listing the known ids.
- A duplicate provider id or served model id refuses the load, naming both extensions.

At most eight providers (declared plus programmatic) are allowed per extension.

## Declared providers

A declared provider descriptor carries `id`, `displayName`, `endpoint`, `secretName`, `modelIds`,
`contextWindow` and optional `supportsTools`. The host reads `secretName` from the extension's
secrets file on first use and puts the value only into the client's request headers. In-process
extensions are fully trusted, so this is hygiene, not a security boundary: the value never has to
reach extension code.

The `model-router` sample declares `router-local` with the models `fast` and `deep`:

```json
{
    "id": "model-router",
    "version": "0.1.0",
    "apiVersion": "^1.10.0",
    "entryAssembly": "ModelRouterExtension.dll",
    "modelProviders": [
        {
            "id": "router-local",
            "displayName": "Router Local",
            "endpoint": "http://127.0.0.1:11434/v1",
            "secretName": "router-key",
            "modelIds": ["fast", "deep"],
            "contextWindow": 32768
        }
    ]
}
```

The secret lives in `~/.lunate/extensions-secrets/model-router.json`:

```json
{ "router-key": "…" }
```

Full manifest:
[`extension.json`](../../../samples/extensions/model-router/ModelRouterExtension/extension.json).

## Using a provider from a tool

Inside a run, `ToolContext.Models` is the run-bound `IModelAccess`. `For(modelId)` resolves the id
through the same registry and charges the call's usage to the owning run, so budgets and the
tree roll-up stay correct:

```csharp
IModelAccess models =
    context.Models
    ?? throw new InvalidOperationException("No model access in this run.");
ChatResponse response = await models
    .For("ext/model-router/auto")
    .GetResponseAsync(
        [new ChatMessage(ChatRole.User, promptElement.GetString())],
        null,
        ct
    );
```

`Models` is null outside a run or when the run has no model registry; refuse with a teaching error
in that case. The `model-router` sample's `ask` tool is the reference:
[`ModelRouterExtensionFactory.cs`](../../../samples/extensions/model-router/ModelRouterExtension/ModelRouterExtensionFactory.cs).

## See also

- Routing and middleware: [model-middleware.md](model-middleware.md), [virtual-models.md](virtual-models.md)
- The model registry contract: [`docs/spec/extensibility.md`](../../spec/extensibility.md#services-for-long-lived-integrations)
- Secrets: [settings-and-secrets.md](settings-and-secrets.md)
