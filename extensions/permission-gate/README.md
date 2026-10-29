# Sample: permission-gate

An extension that enforces a settings-driven policy on tool calls before the approval prompt:
destructive tools are blocked outright (`block`, the default) or fall through to the normal
approval prompt (`confirm`). Everything else proceeds unchanged.

## Core capabilities proven

| Capability | Where |
| --- | --- |
| `ToolCalling` hook with block semantics | `PermissionGateToolCallingHandler` returns `ToolCallingResult.Block` with a reason; the agent loop turns it into a denial-style result and never runs the tool. |
| Tool annotations reaching extensions | `ToolCallingPayload.Annotations` carries the resolved tool's lowercase kebab wire names (`read-only`, `destructive`, `idempotent`, `open-world`), mapped once by `Lunate.Agent.ToolAnnotationNames`. |
| Pre-approval ordering | The hook fires before `IToolApprover`; in `block` mode the approver is never consulted. |
| Settings | `mode` is read through `IExtensionSettings.TryGet` from `~/.lunate/extensions-settings/permission-gate.json`; absent means `block`. |
| Testing kit | `PermissionGateExtension.Tests` loads the built extension through the real loader in a temp directory, replays committed fixtures and drives a `ScriptedApprover`. |

## Layout

```text
samples/extensions/permission-gate/
  PermissionGateExtension/        the extension: manifest, factory, ToolCalling policy
  PermissionGateExtension.Tests/  kit-based tests and the replay fixtures
  README.md
```

## Policy

For a call whose annotations include `destructive`:

- `mode=block` (default): `ToolCallingResult.Block` with
  `permission-gate: destructive tool '<name>' blocked - set mode=confirm to approve interactively`.
- `mode=confirm`: `ToolCallingResult.Proceed(null)` — arguments unchanged, the call reaches the
  normal approval prompt.

All other calls proceed unchanged. The handler logs its decisions through `IExtensionLog`.

## Copy and extend

1. Copy this directory, rename the projects and namespaces, and update `extension.json`
   (`id`, `entryAssembly`, `apiVersion`, `hooks`, `settingsSchema`).
2. Keep the tests: `PermissionGateExtension.Tests` installs the built extension into a temp
   directory, so tests never touch the real `~/.lunate`. Add your own annotated test tools and
   record a fixture for the model side (see `Lunate.Ai` fixtures); do not weaken the assertions.
3. Policy handlers should decide on the payload's annotations, never on tool-name lists — the
   annotations are the contract the model-side tool declarations carry.

See `templates/extension/README.md` for the full manifest and hook wiring reference.
