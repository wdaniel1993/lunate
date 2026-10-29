# Design — first-party extensions

- Status: proposed — 2026-10-29
- Change: `first-party-extensions` (#242, milestone 0.2)
- Implements: the maintainer's ruling on #242 (2026-10-27): a supported extension tier beside
  `samples/`, starting with `subagents`, `lsp-diagnostics`, `permission-gate`.
- Accepted decisions: this change's ADR-0023 ("what supported promises").
- Diagrams: unattended run; the grill-me interview rules could not be answered (no human watching), so
  every open question below is resolved against the ruling as written and flagged on the PR for the
  maintainer to correct.

## 1. Resolved decisions

| Branch | Resolution |
| --- | --- |
| Tier directory name | `extensions/` at the repository root, beside `samples/`. `docs/extensions/` (authoring prose) already owns the name "extensions" inside `docs/`; the repo-root directory has no collision and matches the issue's wording ("a supported extension tier beside `samples/`"). |
| Tier layout | `extensions/<id>/<Name>Extension/` and `<Name>Extension.Tests/`, exactly the layout the samples use. No restructuring while moving: the ids, namespaces, manifest ids and tool names have never changed and must not change now. |
| Tier README | `extensions/README.md` states the contract: what a user gets (install, rely on it), the promotion bar (public API only, tests in the gate, packaged as `Lunate.Extensions.*` and published on the release train, product-page readme), and what the tier does not mean (no privileged runtime scope; publishing is the release workflow's job; promotion is the maintainer's decision). It records the unpromoted extensions with the maintainer's reasons (model-router: needs a configuration surface that does not exist; the rest exist to demonstrate an API). |
| The two new packages | `lsp-diagnostics` and `permission-gate` get the same bundle packaging `SubagentsExtension.csproj` already has: `IsPackable=true`, `PackageId=Lunate.Extensions.LspDiagnostics` / `Lunate.Extensions.PermissionGate`, no host assembly in the package, package readme, MIT licence, repository links, NU5100/NU5128 suppressed for the bundle layout. Only the packaging differs from their sample csproj; the C# code is untouched. |
| Package README | The csproj packs the tier README that sits one level up (`extensions/<id>/README.md`) as the package's product page, keeping the one-README-per-extension rule the samples already follow. |
| Manifest versions | All three keep `extension.json` version `0.1.0` and their existing `apiVersion` ranges: this move changes nothing the versions describe, and bumping them is publishing work (#237 owns the release publishing step this change deliberately does not touch). |
| Tests in the gate | The three test projects are already members of `lunate.slnx`, so the gate's solution-wide `dotnet test` (the default CI path) runs them. The move keeps that true; the solution folders are renamed to follow the new paths. The change adds no new test classes: nothing observable about the extensions' behaviour moves — the ownership change is proven by the existing suites passing at the new paths. |
| Gate scripts | `scripts/verify.*`'s extension-package roundtrip packs `subagents` from its new path (`extensions/subagents/SubagentsExtension/…`). Packing all three in the gate is the tag-publishing follow-up (#237 → #242) already announced to #242, not this change. `scripts/render-media.sh` follows the moved `permission-gate` path. |
| Docs | `docs/` links to the moved directories are re-pointed (`doc/extensions/contributions/*` file links, `docs/extensions/testing.md`, `docs/extensions/packages.md`, `docs/extensions-remote.md`'s fitness-suite mention). The fitness-suite table in `docs/spec/extensibility.md` moves the three promoted rows onto the tier section; the twelve contribution pages keep proving the APIs. |
| OpenSpec deltas | `extensions` gains the supported-tier requirement (above); `distribution`'s *Extension package* requirement follows the source move. Both are one-change tokens; nothing normative about publishing changes here. |
| CI path filters | `ci.yml` needs no per-path filter (its plan is generated from the diff, full gate by default); `docs-lint.yml` and `ci.yml` paths-ignore glob prose only. The change is code + docs mixed, so it takes the full gate as before. |

## 2. What does not change

- Runtime: zero. No C# file under `src/` changes; loader, discovery, trust, settings and installing are
  untouched. An installed user cannot tell this PR happened, except the README a package now
  ships for `lsp-diagnostics` / `permission-gate` once those land on the feed (#237).
- Sample tier: the seven remaining samples stay exactly as they are; their READMEs stay "what it
  proves" documents.
- Release workflow: `.github/workflows/release.yml` is untouched by this PR (the card forbids it). The
  pack path it holds (`samples/extensions/subagents/…`) goes stale at merge; the publishing redo (#237)
  owns fixing it, and #242's tag-publishing of the two new packages follows that. This is called out on
  the PR body, the PR comments and the issue.

## 3. Risks

| Risk | Handling |
| --- | --- |
| A stale reference to the old paths survives (build green, repo broken on the next full verify) | Grep-driven sweep over the whole tree for `samples/extensions/{subagents,lsp-diagnostics,permission-gate}`; recorded paths (archives, spikes, changelog) are left alone per the house rule that records keep what was true when written. |
| `dotnet pack` of the two new bundles produces a package a user cannot install | The verify gate's `check-package-metadata.sh` already rejects licence/readme/repository gaps; it runs over the subagents package today and the two new packages adopt the same shape. |
| Tag cut between merge and #237 publishes from the stale `release.yml` path | Announced on the PR and the issue; the maintainer decides whether to cut a tag before #237 lands. |
