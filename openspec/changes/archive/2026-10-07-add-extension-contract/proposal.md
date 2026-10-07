# add-extension-contract

## Why

Part B of the extensibility architecture (ADR-0017) starts here. Extensions must compile against a small, semver'd contract assembly — never against `Lunate.Agent` internals — and the host must be able to discover, load, unload, configure and trust them. This is card T-36: contract assembly + ALC sharing, manifest, lifecycle, settings and secrets, with the done-gate "contract assembly loads; startup budget unchanged with 10 installed extensions".

## What Changes

- **New contract assembly** `Lunate.Extensibility.Abstractions` (semver'd, PublicAPI-tracked, depends only on `Microsoft.Extensions.AI.Abstractions`): the extension entry point (`IExtensionFactory`, `IExtension`), the context (`IExtensionContext` with id, settings, secrets, log), the manifest model (`ExtensionManifest`), the API-version range matcher (minimal grammar, no new packages) and the current API version.
- **New host library** `Lunate.Extensibility`: discovery of `~/.lunate/extensions/` (global) and `.lunate/extensions/` (project), manifest reading at startup, lazy assembly loading into a collectible `AssemblyLoadContext` (sharing the abstractions, `Microsoft.Extensions.AI.Abstractions` and `System.Text.Json`; everything else private), factory registration, unload; per-extension settings (validated against a documented schema subset) and namespaced secrets (never written to session files); project-extension trust: one-time approval per repository identity, re-prompted when the content hash changes.
- **Startup budget**: manifest scanning with 10 installed extensions is a test with an explicit tripwire; the CLI startup budget is unchanged (no host integration yet).
- Spec: new `extensions` capability with the contract, manifest, loading, discovery, trust, settings and secrets requirements.

## Impact

- Two new projects (`src/Lunate.Extensibility.Abstractions/`, `src/Lunate.Extensibility/`) + their test projects; solution wiring; the PublicAPI gate learns the abstractions assembly.
- No changes to existing projects; **no new NuGet packages**; no golden changes; no new ADR (implements accepted ADR-0017).
- Out of scope (later cards): the hook runner (T-37), tool registration (T-38), services (T-39), UI (T-40s), MCP, the out-of-process host (T-52).
