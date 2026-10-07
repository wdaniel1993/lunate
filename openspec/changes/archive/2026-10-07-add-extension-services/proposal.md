# add-extension-services

## Why

Card T-39 (deps T-37): extension **services** — background services with lifecycle, the file change bus, and the model and service registries. T-37 delivered the lifecycle hooks (`SessionStarted`/`SessionEnding`) and the runner they fire through; this change gives extensions the long-lived machinery those hooks exist to start and stop, and the event/registry surfaces the reference extensions (LSP, memory, model-router) consume. The card's mutation-queue half already landed with `add-extension-formats` (per-path read-modify-write); this change integrates and verifies it, not re-implements it. Done-gate: a fake LSP server lifecycle test; file bus events after write/edit.

## What Changes

- **Background services** (abstractions, semver-minor): `IBackgroundService` (`StartAsync`/`StopAsync`) + registration on `IExtensionContext`; the host starts services through the runner lifecycle (`SessionStarted` semantics: safe to run more than once; `SessionEnding` idempotent), tracks state per extension, and unloads with the ALC. A service that fails to start is reported and its extension's remaining services do not start (declared policy).
- **File change bus**: a core-emitted `FileChanged` notification (path + workspace id) fired after successful `write`/`edit` mutations; extensions subscribe with a handler interface; delivery is in-process, ordered per workspace, and failures are reported (never thrown to the mutation path). The workspace id is the `WorktreeRoot` from `add-workspace-roots`.
- **Service registry**: extensions register named services and look them up by name; the registry exposes **core services** to extensions (file bus subscription, mutation queue as a handle, workspace info) — handles and names, never live objects across the boundary except the registered service interfaces themselves (in-process trust model, ADR-0017).
- **Model provider registry** (declaration level): extensions declare model providers in the manifest (`modelProviders`: id, display name, endpoint, secret name, model ids); the host validates and lists them; instantiation into `IChatClient`s lands with the model-router sample card. 
- **Mutation queue integration**: write/edit already route through `IFileMutationQueue`; the file bus fires only after a successful queued mutation (one event per mutation).
- Spec: `extensions` capability gains services, file bus and registry requirements.

## Impact

- `Lunate.Extensibility.Abstractions` (semver-minor): service/bus/registry surfaces + manifest `modelProviders`.
- `Lunate.Extensibility`: service host, file bus, registries (+ tests with a fake LSP server).
- `Lunate.Agent`: `IFileChangeSink` seam (host-injected, no-op default); `Lunate.Coding`: write/edit emit through the sink after successful mutations.
- No new packages; no golden changes; no new ADR (implements accepted ADR-0017).
