# SessionStarted semantics

## Why

`SessionStarted` fires **during the first extension's load** (`ExtensionLoader.Load.cs` calls `StartSessionAsync` inside the per-extension load, and its once-guard makes the first load the firing point). Consequences, reproduced with two fixtures (`consumer` sorts before `hello`):

- Handlers registered by any **later-loaded** extension never observe the event (`HookRunner.Register` has no replay; the event runs once). `hello`'s own `SessionStarted` handler line is absent in every two-extension run, present in every single-extension run.
- A handler that does fire runs with only the extensions loaded **so far** visible — cross-extension service lookups at session start are order-dependent (`consumer` sees `provider=False` at its own load).

`SessionEnding` is unaffected (fires once at the end, when every handler is registered).

## What changes

- The event fires **once, from the composition, after every extension discovered at session start has loaded** — every registered handler observes it, with the full extension set and all background services started.
- An extension loaded **later** (a runtime reload) does not observe the event; that is the documented semantics, not an accident.
- Per-extension loading still starts that extension's background services and emits the tools-changed notification at its own load; only the event's firing point moves.
- A two-extension regression test pins both halves (both observe; a late load does not).

## Impact

- `src/Lunate.Extensibility/ExtensionLoader{,.Load,.Remote}.cs` (firing point; `StartSessionAsync` becomes callable from the composition), `src/Lunate.Coding/ExtensionSession.cs` (one call after the load loop; its cancellation cleanup covers the dispatch), `src/Lunate.Extensibility.Testing/ExtensionTestHost.cs` (the kit mirrors the composition's explicit call); tests.
- `openspec/specs/extensions/spec.md` — the `Hook wiring` requirement's `SessionStarted` clause gains the firing rule (MODIFIED) plus two scenarios.
