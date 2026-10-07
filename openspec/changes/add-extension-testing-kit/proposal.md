# add-extension-testing-kit

## Why

Card T-42 (deps T-37): extension authors need a first-class way to host and test an extension without the full CLI, and the fitness suite cards are about to depend on one. `docs/spec/extensibility.md` ("Trust, performance, testing") already specifies the shape: "`Lunate.Extensibility.Testing`: test host with `ReplayChatClient`, scripted user interaction, event and session assertions. The template project uses it." This change implements that spec text — no new behavior is specified, so this change carries **no spec deltas** (`skip_specs`). Done-gate: the template extension builds and loads in CI.

## What Changes

- **New project `src/Lunate.Extensibility.Testing/`**: `ExtensionTestHost` (loads an extension from a directory through the real `ExtensionLoader` with injectable temp stores and the real runner; `Start`/`Stop` session lifecycle), `RecordingExtensionLog`, `ScriptedTrustPrompt`, `ScriptedApprover` (scripted tool-approval), `EventRecorder` (typed queries over `AgentEvent` streams), and session assertion helpers (count entries by kind after a run). The chat client comes from `Lunate.Ai` — the existing `ReplayChatClient` (recorded fixtures under `tests/fixtures/streams/`) — passed by the author; no new client code.
- **Template project `templates/extension/`**: a starter (`TemplateExtension/` referencing only the abstractions: factory + one `ToolCalling` handler + one background service + settings usage; `extension.json`; README) plus `TemplateExtension.Tests/` that uses the kit (loads the built template from a temp directory, runs it against a replayed fixture, asserts the hook fired and the service lifecycle ran). Both projects join the solution, so the template **builds and loads in CI**.
- No spec deltas: the behavior is the already-specified testing-kit text; the template demonstrates it.

## Impact

- New `src/Lunate.Extensibility.Testing/` (+ `tests/Lunate.Extensibility.Testing.Tests/`), template under `templates/extension/`; solution wiring; layering gate learns the testing edges.
- No changes to existing src projects; no new NuGet packages; no golden changes; no new ADR (implements accepted ADR-0017 + existing spec text).
