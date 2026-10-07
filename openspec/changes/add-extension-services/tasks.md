# Tasks

## 1. Contract surfaces (TDD, red-first)

- [x] 1.1 `IBackgroundService` (`StartAsync`/`StopAsync`), `IFileChangedHandler` (handler interface, not a delegate), `IExtensionContext` gains `RegisterService(name, service)`, `SubscribeFileChanged(handler, pattern?)`, `TryGetService(name)` / typed lookup for core services; API version bump as semver-minor; PublicAPI entries
- [x] 1.2 Manifest: `modelProviders` array (id, displayName, endpoint, secretName, modelIds) validated per design; malformed entries name file + field
- [x] 1.3 JSON conformance for the new DTOs (file change payload, model provider descriptor)

## 2. Service host and lifecycle

- [x] 2.1 Host starts registered services on `SessionStarted` semantics (safe to run more than once), stops them idempotently on `SessionEnding`; per-extension state tracked; unload drops them
- [x] 2.2 Start failure: reported, remaining services of that extension do not start (policy per design); stop failures reported
- [x] 2.3 Fake LSP server fixture: lifecycle test — start, serve a request over the fixture's transport, stop; restart-safe

## 3. File change bus

- [x] 3.1 `IFileChangeSink` seam in `Lunate.Agent` (no-op default); write/edit emit one event after each successful queued mutation, carrying the canonical path
- [x] 3.2 Bus in `Lunate.Extensibility`: workspace id on every event (`WorktreeRoot`), per-workspace ordering, handler failures reported (never thrown to the mutation path); pattern filter per design
- [x] 3.3 Tests: events after write and edit; none after a refused/failed mutation; ordering under the queue; workspace id correct across two worktrees

## 4. Registries

- [ ] 4.1 Service registry: named registration + lookup, duplicate names refused (naming both extensions); core services exposed to extensions per design
- [ ] 4.2 Model provider registry: declared providers validated, listed; secrets referenced by name only (never values); duplicate provider ids refused
- [ ] 4.3 Tests for both registries including the refusal paths

## 5. Close

- [ ] 5.1 `dotnet csharpier format .`; `bash scripts/verify.sh` green (incl. de-AT); `openspec validate add-extension-services --type change --strict`; self-review; commit per group
