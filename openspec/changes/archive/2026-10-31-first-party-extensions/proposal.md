## Why

Lunate's extension platform ships extension code in two places: `templates/` for a starting point and
`samples/` for "capabilities proven". Samples are demos a user copies to learn the API; nothing tells a
user which extensions they can install and rely on. The maintainer's ruling (2026-10-27, #242): a
**supported** extension tier lives beside `samples/`, starting with `subagents` (already shipping as
`Lunate.Extensions.Subagents`), `lsp-diagnostics` and `permission-gate`. `model-router` and the other
samples are explicitly not promoted: model-router needs a configuration surface that does not exist, and
the rest exist to demonstrate an API.

The bar for "supported" (verbatim from #242): a user installs it for what it does, not to learn the API;
it uses only the public extension API; it has tests in the gate; it is packed and published as
`Lunate.Extensions.*`; its README is a product page (what it does, how to configure it), not a
"capabilities proven" table.

## What Changes

- A new tier directory `extensions/` beside `samples/`, with a README stating the tier contract — what
  "supported" promises (bug fixes, API compatibility at the published API version, packaged on the
  release train, product-page documentation) and what it does not (publishing is owned by the release
  workflow; a supported extension is not core — it still loads through the same loader, the same trust
  flow and the same public contract as any extension).
- `git mv` of three extensions from `samples/extensions/` into `extensions/`, keeping their project
  layout, namespaces and manifest ids: `subagents` (`Lunate.Extensions.Subagents`, already packaged),
  `lsp-diagnostics` and `permission-gate` (both become packable bundles: `PackageId`, `IsPackable`,
  the bundle pack target, a `lunate.package.json` and a package README — the same shape the subagents
  package already has).
- The three READMEs are rewritten as product pages: what the extension does, how to install and
  configure it. The API-proof material ("what it proves") stays in `docs/extensions/contributions/*`
  and `docs/spec/extensibility.md`'s fitness-suite table, re-pointed at the new paths.
- Solution, gate scripts and docs that reference the moved paths follow the move (`lunate.slnx`,
  `scripts/verify.*`'s extension-package roundtrip, `scripts/render-media.sh`, `docs/` links).
- OpenSpec follow-through: the `distribution` requirement naming `samples/extensions/subagents` is
  modified to name `extensions/subagents`; the `extensions` spec gains the supported-tier requirement.
- An ADR records what "supported" promises (ADR-0023).
- **Not in this change**: the release workflow's publishing step (`.github/workflows/release.yml`'s
  pack/publish) — publishing the contract packages is #237's, and this issue's tag-publishing of the two
  newly packable extensions follows it. The `release.yml` path this move makes stale is called out on
  the pull request and the issue.

## Capabilities

### New Capabilities

_none_

### Modified Capabilities

- `extensions`: a new requirement states the supported tier — the directory, the bar an extension must
  meet to be promoted (what "supported" promises and what it does not), and that a supported extension
  is an ordinary extension at load time.
- `distribution`: the requirement publishing the reference extension package is modified to follow the
  extension's source move (`extensions/subagents`); the verify gate packs the extension from its new
  path.

## Impact

- Repository layout: `extensions/{subagents,lsp-diagnostics,permission-gate}/`, `samples/extensions/`
  keeps the remaining seven fitness-suite samples.
- `lsp-diagnostics` and `permission-gate` csproj files gain bundle-packaging metadata (package ids
  `Lunate.Extensions.LspDiagnostics` / `Lunate.Extensions.PermissionGate`, `IsPackable=true`, no host
  assembly inside the bundle); `subagents` keeps its existing package id.
- Tests: no behaviour changes; the three test projects move with the extensions and stay in the gate
  (they are already solution members of `lunate.slnx` and so run in the gate's solution-wide
  `dotnet test`).
- No public API changes; the three extensions already compile against `Lunate.Extensibility.Abstractions`
  and the core tool types it carries, nothing internal.
- Docs updated where they referenced the moved paths; the fitness-suite table in
  `docs/spec/extensibility.md` keeps the capabilities the samples prove and moves the three promoted
  rows onto the tier.
