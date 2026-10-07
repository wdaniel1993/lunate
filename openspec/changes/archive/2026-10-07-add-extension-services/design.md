# Design: add-extension-services

## Context

T-37 delivered the lifecycle hooks and runner; T-36 the loader/trust. `docs/spec/extensibility.md` ("Services", "Per-workspace services" in ADR-0017) is the detail spec: services keyed by workspace, `FileChanged` carrying the workspace id, Roslyn capped (T-25 wires its own limits). The mutation queue exists (`add-extension-formats`); this change only connects it to the bus. No new packages; no golden changes; no new ADR.

## Background services (contract, semver-minor)

```csharp
public interface IBackgroundService { ValueTask StartAsync(CancellationToken ct); ValueTask StopAsync(CancellationToken ct); }
public interface IFileChangedHandler : IHookHandler { ValueTask OnFileChangedAsync(FileChangedPayload p, CancellationToken ct); }
```

- `IExtensionContext` gains `RegisterService(string name, IBackgroundService service)`, `SubscribeFileChanged(IFileChangedHandler handler, string? pathPattern = null)`, and `TryGetService(string name, out IBackgroundService service)` for services other extensions registered. Core services are exposed as named handles per design (below). All registration happens at `Create` (registration only).
- Lifecycle: the host starts services through the runner's `SessionStarted` semantics (safe more than once — started services are not restarted) and stops them idempotently on `SessionEnding`. Unload drops the extension's services.
- **Start-failure policy** (pinned): a service that throws in `StartAsync` is reported with the extension id and service name; its extension's remaining unstarted services do not start; already-started services of that extension are stopped best-effort. `StopAsync` failures are reported, never thrown to the lifecycle caller.
- `FileChangedHandler` priority/order follow the runner (priority descending, stable load-order tie-break) — the bus dispatches through the same ordered pipeline.

## File change bus

- **Emission**: `Lunate.Agent` gains `IFileChangeSink { void Notify(string absolutePath); }` — a host-injected seam with a no-op default (`AgentHarnessOptions.FileChanges`). `Lunate.Coding`'s `write`/`edit` call the sink **after** a successful queued mutation completes (inside the mutation queue section, one event per mutation). Refused, failed or queued-but-throwing mutations emit nothing.
- **Delivery**: the bus lives in `Lunate.Extensibility` (implements the sink). Every event carries `(path, workspaceId)` where `workspaceId = WorktreeRoot` (from `add-workspace-roots`); ordered per workspace (single dispatcher queue per workspace id); handler failures and timeouts follow the hook failure policy (report), never reach the mutation path.
- **Pattern filter**: optional `pathPattern` per subscription; grammar: `*` matches any characters, `?` one, case rules per platform (documented; no regex).
- **Workspace**: the bus resolves the workspace id from the configured workspace; a mutation outside the workspace (explicit extra root) still emits with the run's workspace id (documented).

## Service registry

- Names are lowercase `[a-z0-9-]+` with an optional `ext/<id>/` prefix convention; **duplicate names are refused** (error naming both extensions; core services are reserved names and cannot be shadowed).
- Core services exposed to extensions (all read-only handles): `core/file-bus` (subscribe), `core/mutation-queue` (schedule a read-modify-write on a path), `core/workspace` (identity: worktree root, repo root, git common dir).

## Model provider registry (declaration level)

- Manifest gains `"modelProviders": [{ "id": "acme-local", "displayName": "Acme Local", "endpoint": "http://localhost:11434/v1", "secretName": "acme-key", "modelIds": ["acme-7b"] }]`.
- Validation: id `[a-z0-9-]+` (namespaced `ext/<extension-id>/<id>`); endpoint absolute http(s); `secretName` references the extension's secrets store by name (values never appear); modelIds non-empty; duplicate provider ids refused across extensions.
- `ModelProviderRegistry` lists declared providers (`List()`); instantiation into `IChatClient` (and routing) lands with the model-router sample card (T-50) — this change does not build clients.

## Testing strategy

- Fake LSP fixture: a minimal service speaking a trivial request/response over an in-memory transport; lifecycle test (start → request → stop), restart-safe, start-failure and stop-failure paths.
- Bus: events after write and edit (real Coding tools over a temp workspace); none after refused mutations; ordering under concurrent queued mutations; two-worktree workspace-id correctness; pattern filter boundaries; handler throw/timeout reported, mutation unaffected.
- Registries: duplicates refused naming both sides; reserved core names not shadowable; provider validation errors name file + field; secrets by name only.
- Both cultures (suite runs under de-AT).

## Out of scope

Model-router client instantiation and routing (T-50), the Roslyn/LSP limits themselves (T-25), MCP servers (T-41), UI interaction (T-40), out-of-process services (T-52).
