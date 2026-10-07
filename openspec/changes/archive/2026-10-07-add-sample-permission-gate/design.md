# Design: add-sample-permission-gate

## Context

Fitness row: "permission-gate | ToolCalling + annotations → block/confirm". The tool model (A1) already declares `ReadOnly`/`Destructive`/`Idempotent`/`OpenWorld` annotations and derives `ToolRisk`; the hook catalogue says `ToolCalling` runs before the built-in approval prompt. What is missing is only the *visibility* of those annotations to hook handlers. Doctrine: "A sample that needs a workaround means the core is missing a primitive." No new packages; no ADR.

## Contract addition (1.3.0)

- `ToolCallingPayload` gains `IReadOnlyList<string> Annotations` — the resolved tool's declared annotations as **lowercase kebab wire names in declaration order: `read-only`, `destructive`, `idempotent`, `open-world`**. No serialized form of annotations exists yet (A1 added the record only), so this change introduces exactly one mapping helper in `Lunate.Agent` next to `ToolAnnotations` (single source of the wire names; the adapter and any future serializer reuse it — never a second casing). Empty list when the tool declares none.
- `ToolResultReadyPayload` is **not** changed (the sample doesn't need it; minimalism).
- Agent seam: the `IAgentHookPoints.ToolCalling` invocation already resolves the tool before approval; the seam record carries the flag values so the adapter can map them. Behavior unchanged when hooks are unconfigured.
- PublicAPI.Unshipped entries; existing pinned tests updated (conformance now checks annotations round-trip).

## Spec delta

MODIFIED `Hook wiring`: `ToolCalling` fires "before the approval prompt, with the resolved tool's annotations on the payload so policy handlers can decide on them". New scenario: a `Destructive` tool's call reaches the hook with `destructive` in the payload; a policy handler blocks or falls through on that basis. The full restated requirement lives in `specs/extensions/spec.md` of this change.

## Sample: `samples/extensions/permission-gate/`

- `PermissionGateExtension/` — abstractions-only csproj; manifest `id: permission-gate`, `apiVersion ^1.3.0`, `settingsSchema`: `mode` (`block` | `confirm`, default `block`); factory; extension registers one `IToolCallingHandler`.
- Policy: for calls whose tool annotations include `destructive` (using the payload's annotations — never name lists): `block` mode → `ToolCallingResult.Block("permission-gate: destructive tool '<name>' blocked - set mode=confirm to approve interactively")`; `confirm` mode → `Proceed(null)` (falls through to the normal approval prompt). Everything else proceeds unchanged. Logs its decisions through `IExtensionLog`.
- `PermissionGateExtension.Tests/` — via the testing kit:
  1. block mode + destructive call → blocked with reason; tool never executes; approval prompt never consulted (ScriptedApprover not called).
  2. block mode + read-only call → proceeds; approval runs as usual.
  3. confirm mode + destructive call → falls through; ScriptedApprover denies → denial-style result; ScriptedApprover allows → executes.
  4. setting drives the mode (block vs confirm from the persisted settings store).
  5. calls with no annotations proceed in both modes.
- `README.md` — capabilities proven (ToolCalling hook + block semantics, tool annotations reaching extensions, pre-approval ordering, settings, testing kit), plus the copy-and-extend note.
- Solution wiring + layering: `PermissionGateExtension -> Lunate.Extensibility.Abstractions` only; `PermissionGateExtension.Tests -> {Testing, Abstractions, Ai}` (fixture replay like the template).

## Testing strategy

- Fixture: drive a destructive tool call and a read-only one through a replay stream (extend/reuse the template's approach; new fixture if needed — recorded via the temporary `RecordingChatClient` pattern, byte-round-trip test covers it).
- Which core tool is "destructive" for the fixture: pick a core tool whose declared annotations include destructive (e.g. the file `write`/`edit` or `bash` — confirm actual annotations in `Lunate.Coding`/tool registrations during apply; the test may also register a custom annotated tool through the harness where the seam allows).
- Both cultures.

## Out of scope

User-interaction scripting beyond the scripted approver (T-40), other samples (T-44+), UI presentation of blocks (later), changing the approval policy itself.
