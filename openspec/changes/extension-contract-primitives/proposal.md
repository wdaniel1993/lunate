## Why

The accepted extension contract (maintainer sign-off 2026-10-29, ADR-0017 addendum) publishes the four
primitives the flagship migration needs — a built-in registration path, per-workspace services,
host-evaluated tool activation and stable aliases — as spec behaviour, but no production code implements
them. Until they exist, the flagship (`Lunate.Roslyn`) stays composed directly by the host
(`src/Lunate.Coding/HarnessFactory.cs:151-172`) and the contract can neither be proven nor migrate.

## What Changes

- The extension API version moves semver-minor: `ExtensionApi.Current` becomes `1.12.0`
  (contract assembly version follows, `PublicAPI.Unshipped.txt` updated).
- `extension.json` gains four optional fields, parsed and validated at manifest load:
  `builtin` (provenance assertion), `workspaceServices` (`name`, optional positive
  `maxLoadedWorkspaces`, default 2), `toolActivation` (closed condition set `always`,
  `workspace-root-has-file`, `workspace-is-git-repository`) and `toolAliases` (shape- and
  reservation-checked at parse; ownership-collisions refuse the load). Refusals name the manifest file,
  the tool key and the field, following the existing parse-error style.
- Built-ins: `ExtensionHostOptions.BuiltIns` carries host-supplied descriptors (manifest plus
  `Func<IExtensionFactory>`); `Discover` folds them before the store scans; a built-in front door
  loads through the same activation tail with no collectible `AssemblyLoadContext`; provenance is
  refused in both lying directions, a discovered id equal to a built-in id is skipped with one
  discovery error naming both sources, and `--no-extensions` composes the built-in set. `/extensions`
  shows scope `builtin` and offers no uninstall/reload for them.
- Per-workspace services: `IWorkspaceServiceFactory<T>` / `IWorkspaceServiceLease<T>` and two new
  `IExtensionContext` methods (`RegisterWorkspaceService`, `AcquireWorkspaceServiceAsync`), with a
  host-side registry implementing the documented lifecycle (lazy per root, pinned leases, LRU eviction
  of unpinned entries, all-pinned waiting, `ObjectDisposedException` after shutdown).
- Activation: a host evaluator for the closed condition set (file-only direct children, no recursion,
  refusing path separators). It is evaluated only at run composition, per workspace; the flagship's
  solution-file gate in `HarnessFactory` is expressed through the same evaluator, so core and extension
  tools share one availability semantics. `/extensions` reports a declared tool whose rule is false as
  `inactive (<condition>): <reason>`.
- Aliases: a declared alias becomes the model-facing name the registry and adapter publish while the
  canonical `<extension-id>__<local>` stays the identity; globally unique, collisions refuse the later
  load naming both owners.

Not part of this change (follow-ups): migrating `Lunate.Roslyn` onto the primitives (#293),
`lunate mcp roslyn` adopting the evaluator (spec §Conditional-tool-availability MCP scenarios — aims to
land with #292's evaluator but #274's server rework is scoped with the migration, see the change log on
the PR), and the stability declaration (#237).

## Capabilities

### New Capabilities

_none_

### Modified Capabilities

- `extensions`: the four published requirements (*Built-in extension provenance*, *Per-workspace
  extension services*, *Conditional tool availability*, *Stable tool aliases*) become true of the code;
  the `/extensions` view requirement is modified to add scope `builtin`, the inactive-tool report and
  the refusal of unload/reload for built-ins.

## Impact

- `Lunate.Agent`: no change beyond tests (activation rule types and the evaluator target
  `Lunate.Extensibility.Abstractions`, which already references the agent types it needs).
- `Lunate.Extensibility.Abstractions` (public, semver-minor): `ExtensionApi.Current`, `ExtensionManifest`
  fields, `IExtensionContext` methods, `IWorkspaceServiceFactory<T>`/`IWorkspaceServiceLease<T>`,
  activation- and alias-model types. All additions appear in `PublicAPI.Unshipped.txt`.
- `Lunate.Extensibility`: loader front doors, `ExtensionHostOptions.BuiltIns`, registries (tool naming
  seam gains the alias, workspace-service registry), composition cross-checks.
- `Lunate.Coding`: composition sites evaluate activation per run; the core cs rule goes through the
  evaluator; `/extensions` view and the extension commands gain `builtin` scope handling.
- Tests: `Lunate.Extensibility.Tests`, `Lunate.Coding.Tests`; a built-in-path fitness extension proves
  the path end to end without touching `Lunate.Roslyn`.
- No new NuGet packages; no `FunctionInvokingChatClient` or `AIFunctionFactory`.
