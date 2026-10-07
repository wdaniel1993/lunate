# Tasks

## 1. Kit (TDD, red-first)

- [x] 1.1 New project `src/Lunate.Extensibility.Testing/` (net10.0, nullable; references Extensibility inputs: Abstractions, Extensibility, Agent, Ai); solution wiring; layering edges in the architecture test
- [x] 1.2 `ExtensionTestHost` per design (real loader, scripted trust, real runner lifecycle, caller-provided `IChatClient`, idempotent stop, unload on dispose)
- [x] 1.3 `RecordingExtensionLog`, `ScriptedTrustPrompt` (exhaustion = failure), `ScriptedApprover`, `EventRecorder`, session assertion helpers
- [x] 1.4 Kit tests: helper semantics; no real-home access; start/stop idempotence

## 2. Template

- [ ] 2.1 `templates/extension/TemplateExtension/` per design (factory, one ToolCalling handler, one background service, one secret by name); `extension.json`; README
- [ ] 2.2 `templates/extension/TemplateExtension.Tests/` using the kit: loads the built assembly from a temp directory, runs against a replay fixture, asserts hook fired + service lifecycle + approval + log id
- [ ] 2.3 Both projects in the solution (they build on all CI runners)

## 3. Close

- [ ] 3.1 `dotnet csharpier format .`; `bash scripts/verify.sh` green (incl. de-AT); `openspec validate add-extension-testing-kit --type change --strict`; self-review; commit per group
