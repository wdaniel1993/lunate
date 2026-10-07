# Design: add-extension-testing-kit

## Context

`docs/spec/extensibility.md`: "`Lunate.Extensibility.Testing`: test host with `ReplayChatClient`, scripted user interaction, event and session assertions. The template project uses it." `Lunate.Ai` already ships `ReplayChatClient`/`RecordingChatClient` (fixture-based) — the kit passes them through, it does not re-implement chat clients. No new packages; no ADR; no spec deltas (`skip_specs`); implements existing spec text.

## `Lunate.Extensibility.Testing`

- `ExtensionTestHost`: given a temp-able store root and an extension directory (or in-memory source files), it builds the directory, runs the **real** `ExtensionLoader` (real manifests, real ALC, real trust with a `ScriptedTrustPrompt`), starts the session through the runner (real service lifecycle), exposes `RunAsync` against a caller-provided `IChatClient` (typically `ReplayChatClient`), and stops idempotently. Disposal unloads.
- `RecordingExtensionLog : IExtensionLog` — ordered entries for assertions.
- `ScriptedTrustPrompt : IExtensionTrustPrompt` — queued up-front decisions; approval/denial counts exposed; exhaustion is a test failure (never silently approves).
- `ScriptedApprover : IToolApprover` — queued allow/deny decisions for tool calls.
- `EventRecorder` — collects `AgentEvent`s from a run; `Of<T>()`, `Single<T>()`, ordering helpers.
- Session assertions: load a written session and count entries by kind (`CountExtensions`, `CountNestedCalls`, …) — reads through the real `Session.Load`.
- All temp state under the caller's temp directory; nothing touches the real `~/.lunate` (asserted in the kit's own tests).

## Template (`templates/extension/`)

- `TemplateExtension/`: csproj referencing **only** `Lunate.Extensibility.Abstractions` (plus MEAI abstractions); `extension.json` (id `template`, a `^1.2.0` apiVersion); `TemplateExtensionFactory`; `TemplateExtension` registering one `ToolCalling` handler (logs + proceed), one background service (lifecycle counters), and reading one namespaced secret by name. Placeholders are real working names — it builds as-is.
- `TemplateExtension.Tests/`: uses the kit — loads the built template assembly from a temp extension directory, runs a session with a `ReplayChatClient` fixture, asserts: hook fired, service started/stopped once, tool call approved via the scripted approver, and the log carries the extension id.
- README: how an author copies the template (`cp -r templates/extension my-extension`), where the manifest lives, and how to add hooks/services.

## CI

- Both template projects join the solution → `verify.sh` builds them on all three runners.
- The template test loads the freshly built assembly through the real loader → "builds and loads in CI" is genuinely exercised, not just compiled.

## Testing strategy

- Kit's own test project (`tests/Lunate.Extensibility.Testing.Tests/`): every helper's semantics (scripted prompt exhaustion fails; recorder ordering; host start/stop idempotence; no real-home access — assert the temp root is used).
- The template test doubles as the kit's integration proof.
- Both cultures (suite runs under de-AT).

## Out of scope

UI/interaction scripting (T-40), fitness samples themselves (T-43+), `dotnet new` template packaging (a later distribution concern, T-32).
