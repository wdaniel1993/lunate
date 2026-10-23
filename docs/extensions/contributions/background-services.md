# Background services

A background service owns a long-lived resource for the session: an LSP server, a watcher, a
connection, an in-memory store. It is registered as a named service and started and stopped by the
host with the session.

## The API

```csharp
public interface IBackgroundService
{
    ValueTask StartAsync(CancellationToken cancellationToken);

    ValueTask StopAsync(CancellationToken cancellationToken);
}
```

```csharp
context.RegisterService($"ext/{context.Id}/server", new LspServerService(settings, context.Log));
```

Registration is registration only. The host starts the services of an extension as that extension
loads, and stops them when the session ends. `StartAsync` may run more than once and must be safe
to repeat; `StopAsync` must be idempotent.

## Lifecycle

- **Start.** The host starts each registered service once. An already-started service is not
  restarted.
- **Start failure.** The failure is logged with the extension id, service name, exception type and
  message. The extension's remaining services do not start, its already-started services are
  stopped best-effort, and the extension stays loaded. Other extensions are unaffected.
- **Stop.** At session end, extensions stop in load order and each extension's services stop in
  reverse start order. A failure is logged and never thrown; the host continues stopping the rest.
- **Unload.** Unloading an extension stops and forgets its services.
- **After start.** The host does not supervise a running service. A failure after `StartAsync`
  returns is the service's own to catch and report — log it or notify, never let it crash the
  session. A service that needs to restart its resource does so on its next use.

Two patterns are common:

- **Eager.** Start the resource in `StartAsync`; stop it in `StopAsync`.
- **Lazy.** `StartAsync` does nothing; the resource starts on first use and `StopAsync` stops it if
  it started. `lsp-diagnostics` uses this so a missing language server costs nothing until the
  first eligible result.

## Naming and lookup

The service is registered under a name, so other extensions can look it up — see
[services.md](services.md). Names are lowercase segments, either a single segment or
`ext/<extension-id>/<name>`. A duplicate name refuses the load. `core/` is reserved.

## Example

`lsp-diagnostics` registers a lazily started language server. `StartAsync` is deliberately empty;
the first diagnostics request starts the process, and `StopAsync` shuts it down gracefully with a
kill fallback. The shape of the service (the process work is in the full file):

```csharp
public sealed class LspServerService : IBackgroundService
{
    public ValueTask StartAsync(CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;

    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        // Shutdown, wait, kill the process tree, dispose; safe to call twice.
    }

    public async Task<IReadOnlyList<Diagnostic>> GetDiagnosticsAsync(
        string absolutePath,
        string text,
        string worktreeRoot,
        CancellationToken ct
    )
    {
        // Start the server on first use, restart a dead one, bound the round trip.
        return [];
    }
}
```

Full file:
[`LspServerService.cs`](../../../samples/extensions/lsp-diagnostics/LspDiagnosticsExtension/LspServerService.cs).

A service that only holds state can leave both methods empty. `memory-provider` registers
`MemoryStore` this way: both `StartAsync` and `StopAsync` return `ValueTask.CompletedTask`. Full
file:
[`MemoryStore.cs`](../../../samples/extensions/memory-provider/MemoryProviderExtension/MemoryStore.cs).

## See also

- Named lookup and core handles: [services.md](services.md)
- Session lifecycle and unload: [lifecycle-and-trust.md](../lifecycle-and-trust.md)
- The `IBackgroundService` contract: [`docs/spec/extensibility.md`](../../spec/extensibility.md#services-for-long-lived-integrations)
