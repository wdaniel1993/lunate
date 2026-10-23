# Model middleware

A middleware wraps an `IChatClient` at one slot in the model pipeline. Use it for cross-cutting
behaviour — counting, logging, request shaping — that should apply to calls without changing the
provider or the model id.

## The API

```csharp
public enum ModelMiddlewareSlot
{
    BeforeRouting,
    AroundBackend,
}

public sealed record ExtensionModelMiddleware(
    ModelMiddlewareSlot Slot,
    int Priority,
    Func<IChatClient, IChatClient> Wrap
);
```

Register it in `Create`:

```csharp
context.RegisterModelMiddleware(
    new ExtensionModelMiddleware(
        ModelMiddlewareSlot.AroundBackend,
        Priority: 100,
        inner => new CountingClient(inner, counters)
    )
);
```

`Wrap` is called when the registry composes a client, not at registration. It receives the client
below it and returns the wrapper. Registration is registration only: do not start resources in the
middleware's constructor.

## Slots

| Slot | Wraps | Sees |
| --- | --- | --- |
| `BeforeRouting` | The client the caller uses | Every call, including calls to a virtual model. |
| `AroundBackend` | Each resolved backend client | Only calls that reach a backend. |

A virtual-model call passes through the `BeforeRouting` chain once and through the chosen
backend's `AroundBackend` chain once. A direct call to a backend skips the virtual model but still
goes through both slots.

## Order and failure

Composition is deterministic: extension load order, then `Priority` ascending, then registration
order. The first entry in that order is the outermost wrapper, so it sees a call first and its
response last.

- At most 16 middlewares per extension.
- A middleware exception fails the request closed with an error naming the owning extension:
  `Model middleware from extension '<id>' failed: … The call failed closed; fix or remove the middleware.`
- An exception raised below the wrapper (a provider failure) passes through unchanged; the
  boundary attributes only the middleware's own failures.
- `Wrap` runs per composed client; do not assume it runs once per session.

## Example

`model-router` registers one `AroundBackend` middleware that counts calls by the backend's reported
model id. The counts are exposed through the extension's `router-counters` service.

```csharp
context.RegisterModelMiddleware(
    new ExtensionModelMiddleware(
        ModelMiddlewareSlot.AroundBackend,
        Priority: 100,
        inner => new CountingClient(inner, counters)
    )
);
```

`CountingClient` is a `DelegatingChatClient` that records `response.ModelId` after each call. Full
file:
[`ModelRouterExtensionFactory.cs`](../../../samples/extensions/model-router/ModelRouterExtension/ModelRouterExtensionFactory.cs)
and its
[`CountingMiddleware.cs`](../../../samples/extensions/model-router/ModelRouterExtension/CountingMiddleware.cs).

## See also

- Providers and model ids: [model-providers.md](model-providers.md)
- Virtual models: [virtual-models.md](virtual-models.md)
- The registry contract: [`docs/spec/extensibility.md`](../../spec/extensibility.md#services-for-long-lived-integrations)
