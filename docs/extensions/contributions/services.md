# Services

A service is a named object other extensions and the host can look up. Register it with
`context.RegisterService`; look it up with `context.TryGetService` or, for the host's reserved
handles, `context.TryGetCoreService<T>`.

A registered service is also a background service: it must implement `IBackgroundService`, and the
host starts and stops it with the session. The lifecycle rules are on
[background-services.md](background-services.md); this page covers naming, lookup and the core
handles.

## The API

```csharp
context.RegisterService("memory-store", store);

bool found = context.TryGetService("memory-store", out IBackgroundService? service);
bool hasWorkspace = context.TryGetCoreService("core/workspace", out WorkspaceInfo? workspace);
```

`TryGetService` returns the registered object through the `IBackgroundService` type. `TryGetCoreService<T>`
returns the host handle under a reserved `core/…` name, or false when the host did not register it.

## Names

A service name is lowercase `[a-z0-9-]+` segments, in one of two shapes:

- a single segment: `memory-store`, `router-counters`;
- an extension-namespaced name: `ext/<extension-id>/<name>`, for example
  `ext/lsp-diagnostics/server`.

The first shape is convenient for a service other extensions are expected to consume. The second
makes ownership explicit and matches the convention in the samples. `core/` names are reserved:
registering one is refused.

A duplicate name across extensions refuses the load; the message names both extensions. A
duplicate within one extension is refused too. A service name is not a tool name and has no
64-character limit.

Lookup from `Create` is order-dependent: extensions load in discovery order, so a service from a
later extension is not registered yet. For a robust cross-extension dependency, resolve it on first
use rather than in the factory.

## Cross-extension calls

The contract type both sides share is the one in `Lunate.Extensibility.Abstractions`. A concrete
class from another extension is not a safe contract: each extension's private dependencies load in
its own `AssemblyLoadContext`, so two copies of a third-party type do not have the same identity.

For a callable cross-extension surface, implement `IJsonService` on the registered object:

```csharp
public interface IJsonService
{
    ValueTask<JsonElement?> InvokeAsync(
        string method,
        JsonElement? arguments,
        CancellationToken cancellationToken
    );
}
```

A consumer casts the `IBackgroundService` it looked up to `IJsonService` and calls it with JSON.
Methods are asynchronous by construction; an unknown method or malformed arguments become an error
result, never a crash. This is the same contract the remote extension host uses, so a service
written this way works in both hosting modes.

## Core handles

The host registers these reserved handles before extensions load:

| Name | Type | Use |
| --- | --- | --- |
| `core/workspace` | `WorkspaceInfo` | `WorktreeRoot`, `RepoRoot`, `GitCommonDir` of the run's workspace. |
| `core/file-bus` | `IFileChangeBus` | Subscribe to successful file mutations; usually called through `context.SubscribeFileChanged`. |
| `core/mutation-queue` | `IMutationQueue` | Schedule a per-path read-modify-write (`RunAsync<T>`). |
| `core/process` | `IProcessRunner` | Run one child (`RunAsync`) or start an interactive child (`StartSessionAsync`). |

`IProcessRunner.RunAsync` takes a `ProcessRunRequest` (file name, argument list, working
directory, optional environment, timeout and output cap) and returns a `ProcessRunResult` (exit
code, stdout, stderr, timeout and cap flags). `StartSessionAsync` returns an `IProcessSession` for
line-oriented stdin/stdout exchange. Pass an explicit environment allow-list when the child must
not inherit Lunate's environment — provider keys included. `EnvironmentScrubber.Build()` builds the
minimal base set plus declared pass-through values and explicit entries; the `code-mode` sample and
the remote host both use it.

The `code-mode` and `worktree-tasks` samples are the reference consumers:
[`CodeModeExtensionFactory.cs`](../../../samples/extensions/code-mode/CodeModeExtension/CodeModeExtensionFactory.cs)
and
[`WorktreeTasksExtensionFactory.cs`](../../../samples/extensions/worktree-tasks/WorktreeTasksExtension/WorktreeTasksExtensionFactory.cs).

## Per-workspace services

Use a per-workspace service for state tied to one canonical worktree root, such as a workspace
backend or language-server connection. Declare each name in the manifest's `workspaceServices`
array, then register a factory during extension creation:

```json
"workspaceServices": [
  { "name": "workspace-cache", "maxLoadedWorkspaces": 2 }
]
```

```csharp
context.RegisterWorkspaceService("workspace-cache", new WorkspaceCacheFactory());
```

The host creates a service lazily on its first acquire for a canonical root. `maxLoadedWorkspaces`
is a positive per-service cap (default 2); the host may lower it. When the cap is reached, the least
recently used unpinned workspace is evicted and its service is disposed. An active lease pins its
workspace, so it is never evicted mid-call; if every entry is pinned, an acquire waits
cancellation-aware. Disposing a lease releases that pin. Shutdown disposes the remaining values.
Eviction disposes only the service value: the host does not sweep or delete files from the workspace.

Acquire a typed lease where the worktree-specific operation runs:

```csharp
using IWorkspaceServiceLease<WorkspaceCache> lease = await context.AcquireWorkspaceServiceAsync<WorkspaceCache>(
    "workspace-cache",
    canonicalWorktreeRoot,
    cancellationToken
);
lease.Value.Refresh();
```

The service name must be declared and registered by the same extension. A registered but undeclared
service refuses the load; a declared but unregistered service is logged and dropped. The contract
and lifecycle are also summarized in [the extensibility spec](../../spec/extensibility.md#workspaces-and-repository-identity).

## Example

`memory-provider` registers its store and hands the same instance to its hook handlers:

```csharp
var store = new MemoryStore(context.Id, context.Settings, context.Log);
context.RegisterService("memory-store", store);
context.Register(new MemoryContextBuildingHandler(context.Id, store, context.Log));
context.Register(new MemoryTurnEndedHandler(store));
```

Full file:
[`MemoryProviderExtensionFactory.cs`](../../../samples/extensions/memory-provider/MemoryProviderExtension/MemoryProviderExtensionFactory.cs).
The service itself implements `IBackgroundService` and does its work in memory for the session.

## See also

- Service registry and core capabilities: [`docs/spec/extensibility.md`](../../spec/extensibility.md#services-for-long-lived-integrations)
- Start and stop semantics: [background-services.md](background-services.md)
- The file-change bus: [file-changes.md](file-changes.md)
