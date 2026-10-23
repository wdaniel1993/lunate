# Virtual models

A virtual model is one model id that routes each request to one of its declared backends. It is
useful when the choice depends on the request — size, task type, cost — rather than on the caller.

## The API

```csharp
public sealed record VirtualModelRegistration(
    string Id,
    IReadOnlyList<string> Backends,
    Func<ModelRouteContext, string> Select,
    int? ContextWindow = null,
    bool? SupportsTools = null
);
```

Register it in `Create`:

```csharp
context.RegisterVirtualModel(
    new VirtualModelRegistration(
        "auto",
        ["fast", "deep"],
        route => RequestChars(route.Messages) > threshold ? "deep" : "fast"
    )
);
```

`Select` runs once per request and returns the backend id to use. It receives a
`ModelRouteContext` with the request's `Messages`, the virtual model's namespaced `VirtualModelId`
and the request's `Options` (null when the caller sent none).

## Naming and validation

- The local id is `[a-z0-9-]+`; the registry publishes it as `ext/<extension-id>/<id>`. Callers use
  that namespaced id.
- A duplicate namespaced id refuses the load, naming both extensions.
- At most eight virtual models per extension.
- `Backends` must contain at least one non-empty id. A backend may itself be a virtual model.
- The chain is validated at first resolve, not at registration: an unknown backend, a cycle and a
  chain deeper than eight all fail with a teaching error naming the chain.
- `Select` must return one of the declared `Backends`. Any other value, or a selector exception,
  fails the request with an error naming the virtual model.
- `ContextWindow` defaults to the weakest backend's window and may only narrow it. Declaring a
  larger window than a backend supports is refused at first resolve.
- `SupportsTools` defaults to every backend supporting tools and may only narrow it. Declaring
  `true` while a backend does not support tools is refused at first resolve.

Because registration only records the model, a virtual model can name backends registered by
another extension; validation happens on the first call, when every extension has loaded.

## Example

`model-router` routes on request size: more than `deepThresholdChars` (default 4000) characters go
to `deep`, everything else to `fast`, both served by the declared provider `router-local`.

```csharp
context.RegisterVirtualModel(
    new VirtualModelRegistration(
        "auto",
        ["fast", "deep"],
        route => RequestChars(route.Messages) > threshold ? "deep" : "fast"
    )
);
```

The tool in the same extension calls `context.Models.For("ext/model-router/auto")`. Full file:
[`ModelRouterExtensionFactory.cs`](../../../samples/extensions/model-router/ModelRouterExtension/ModelRouterExtensionFactory.cs).
The model id is `ext/model-router/auto` because the manifest id is `model-router` and the local id
is `auto`.

## See also

- Providers and model ids: [model-providers.md](model-providers.md)
- Middleware slots: [model-middleware.md](model-middleware.md)
- The registry contract: [`docs/spec/extensibility.md`](../../spec/extensibility.md#services-for-long-lived-integrations)
