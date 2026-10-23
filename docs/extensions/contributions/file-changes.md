# File-change subscriptions

The core publishes one notification after every successful `write` or `edit` mutation. An extension
subscribes with `context.SubscribeFileChanged` and receives a `FileChangedPayload` for each matching
change.

This is a subscription, not a `context.Register` hook: the handler is an `IFileChangedHandler`, and
the subscription lives for the extension's lifetime (disposed on unload).

## The API

```csharp
context.SubscribeFileChanged(handler);                  // every change
context.SubscribeFileChanged(handler, "*.cs");          // matching changes
context.SubscribeFileChanged(handler, "/repo/src/*.cs"); // the pattern matches the whole path
```

```csharp
public interface IFileChangedHandler : IHookHandler
{
    ValueTask OnFileChangedAsync(FileChangedPayload payload, CancellationToken cancellationToken);
}
```

`FileChangedPayload` carries:

| Member | Meaning |
| --- | --- |
| `Path` | The canonical absolute path. |
| `RelativePath` | The path relative to the workspace. |
| `WorkspaceId` | The workspace that produced the change. |
| `RunId` | The run that made the change; null outside a run. |
| `Kind` | `created` or `modified`. |
| `Source` | `tool` for the tool path. |

## The pattern

`*` matches any characters (including path separators) and `?` matches exactly one. The pattern is
anchored on the whole canonical absolute path, so `*.cs` matches a `.cs` file anywhere while
`/repo/src/*.cs` matches only under that directory. Comparison is case-sensitive on Linux and
case-insensitive elsewhere. A null or empty pattern matches every change.

## Delivery and limits

- **What is published.** Tool `write` and `edit` mutations only. `bash` mutations and edits made
  outside the tool path are not published.
- **Order.** Notifications are delivered in order per workspace.
- **Timeout.** Each handler has the standard 5-second per-handler timeout. A timeout or exception
  is logged and delivery continues; the failing handler does not stop later subscribers.
- **Session end.** The final drain delivers what was already queued, then ends delivery
  one-way. A notification arriving after the drain is dropped with a log line.
- **Registration only.** `SubscribeFileChanged` registers the handler; it must not read files,
  start processes or start timers.
- **Core handle.** The subscription needs `core/file-bus`. In a host without it, registration logs
  a warning and the handler receives nothing.

## Example

There is no sample under `samples/extensions/` that subscribes to file changes today. The loader
fixture used by the extension tests is the reference:

```csharp
public sealed class HelloFileChangedHandler(IExtensionLog log) : IFileChangedHandler
{
    public int Priority => 0;

    public ValueTask OnFileChangedAsync(
        FileChangedPayload payload,
        CancellationToken cancellationToken
    )
    {
        log.Info(
            $"hello file changed: path={payload.Path}, relative={payload.RelativePath}, workspace={payload.WorkspaceId}, run={payload.RunId ?? "-"}, kind={payload.Kind}"
        );
        return ValueTask.CompletedTask;
    }
}
```

Registration, from the same extension:

```csharp
context.SubscribeFileChanged(new HelloFileChangedHandler(context.Log), "*.cs");
```

Files:
[`HelloFileChangedHandler.cs`](../../../tests/TestExtensions/HelloExtension/HelloFileChangedHandler.cs)
and
[`HelloExtensionFactory.cs`](../../../tests/TestExtensions/HelloExtension/HelloExtensionFactory.cs).

## See also

- The bus contract and per-workspace delivery: [`docs/spec/extensibility.md`](../../spec/extensibility.md#file-change-bus)
- Background services for watchers and servers: [background-services.md](background-services.md)
