# Publish the extension contract as NuGet packages

Refs #237 (umbrella #182). Implements the ADR-0002 addendum's publication step: the contract
becomes installable from nuget.org instead of only buildable in this repository. Unblocked by
#387 (the `Lunate.Contracts` extraction) and #374 (`IDecisionService` in the published surface),
both closed, and by #293's conditions 1–4, verified green in change `roslyn-built-in-migration`
(and in `docs/guide.md`, which now carries the "built only on the public extension API" sentence).

## Why

The packaged-extension template and every matched extension reference the contract through
project references into this repository — the contract is not installable, so "the extension
author can test without this repository" is only true for someone holding a checkout. #237
publishes the extension contract as packages on the same release tag the tool already uses, so an
extension author installs `Lunate.Extensibility.Abstractions` from NuGet and builds their
extension from a feed, not from a source checkout.

## What Changes

- **`Lunate.Contracts` and `Lunate.Extensibility.Abstractions` publish as stable packages**,
  versioned in lockstep with `ExtensionApi.Current` (`1.15.0`), carrying the package metadata the
  tool package established in #218/#235: licence expression, packed readme, icon, repository URL,
  SourceLink and a deterministic build.
- **`Lunate.Agent` and `Lunate.Extensibility.Testing` publish as preview packages** at
  `0.2.0-preview.1`, with "no API stability promise before 1.0" in the package description and
  README; they exist so the harness can be embedded and an extension author can test against a
  feed, not as supported surfaces.
- **The release workflow packs and pushes all of them** on the same `v*` tag through the tool's
  existing Trusted Publishing (OIDC) path, checks each nupkg with
  `scripts/check-package-metadata.sh` before push, and attaches them to the GitHub release.
- **The packaged-extension template installs the contract from NuGet** (package references to the
  two stable contract packages, compile-only), falling back to project references inside this
  repository where the packages are not on the machine's feed. The testing kit page's
  "reference the project" instructions move to the package.
- **The metadata gate grows**: `check-package-metadata.sh` also refuses a package whose icon is
  declared-but-missing or missing entirely, and a `--prerelease` flag pins "this package must say
  preview" so the acceptance is checked where it ships.

## Capabilities

### New Capabilities
- (none — the publication target, versions and deprecation wording are recorded in design.md and
  land in the existing capabilities below)

### Modified Capabilities
- `distribution`: the release carries the contract packages beside the tool and the extension
  package; the dry-run see the full set as artifacts and pushes none; the metadata gate covers
  every published package.

## Impact

- `src/Lunate.Contracts`, `src/Lunate.Extensibility.Abstractions`, `src/Lunate.Agent`,
  `src/Lunate.Extensibility.Testing` become packable with package metadata and (for the preview
  pair) self-contained `lib/` contents; no public API changes, so `PublicAPI.Unshipped.txt` is
  untouched.
- `.github/workflows/release.yml` packs and pushes five packages and its reference-extension path
  is fixed (`samples/extensions/subagents` no longer exist — the bundle lives at
  `extensions/subagents`, which is what `scripts/verify.sh` packs).
- `scripts/check-package-metadata.sh` and `scripts/verify.sh` grow the package roundtrip the
  release will run.
- `templates/extension/TemplateExtension.csproj` and `tests/Lunate.Coding.Tests/ExtensionTemplateTests.cs`
  carry the NuGet reference shape; docs generated from the template snippet move with it.
- Accepted decisions this change does not reopen: `Lunate.Contracts`/`Abstractions` versioned in
  lockstep under `ExtensionApi` (ADR-0002 addendum); publication order after #293 conditions
  (ADR-0026, verified).
