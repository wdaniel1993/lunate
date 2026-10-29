# Tasks — first-party-extensions

The ruling (#242, 2026-10-27) and this change's design decide every branch; the run is unattended, so
any new question is flagged on #242 instead of being invented locally. Test-first applies to behaviour:
this change moves files and changes packaging metadata, so the gate is the proof — the moved suites
must pass unchanged at their new paths, and the two new packages must survive
`check-package-metadata.sh`.

## 1. The proposal lands

- [x] 1.1 Proposal, spec deltas (`extensions`, `distribution`), design, ADR-0023 and the ADR manifest
      are written through the opsx workflow and committed.

## 2. The tier exists

- [x] 2.1 `git mv samples/extensions/subagents extensions/subagents`,
      `git mv samples/extensions/lsp-diagnostics extensions/lsp-diagnostics`,
      `git mv samples/extensions/permission-gate extensions/permission-gate`; project references and
      the three `.csproj` internals follow the depth change.
- [x] 2.2 `lsp-diagnostics` and `permission-gate` adopt the subagents bundle packaging: `IsPackable`,
      `PackageId=Lunate.Extensions.LspDiagnostics` / `Lunate.Extensions.PermissionGate`, no host
      assembly in the package, package readme, licence, repository metadata, NU5100/NU5128 notes; each
      gains a `lunate.package.json` naming its extension directory. `subagents`' csproj content is
      unchanged except paths; the tier README it packs one level up is the new product page.
- [x] 2.3 `lunate.slnx` folders and paths follow the move.

## 3. The gate and the tools follow

- [x] 3.1 `scripts/verify.sh` and `scripts/verify.ps1` pack the subagents extension from its new path
      (the roundtrip and the metadata check unchanged otherwise).
- [x] 3.2 `scripts/render-media.sh` follows the moved `permission-gate` path.
- [x] 3.3 The whole-tree grep sweep: no live reference names the moved `samples/extensions/…` paths
      (records under `openspec/changes/archive/**`, `docs/spikes/**`, `CHANGELOG.md` keep theirs; the
      release workflow's publishing step is #237's and is only reported, not edited here).

## 4. Product pages

- [x] 4.1 `extensions/README.md`: what the tier means, the promotion bar, what "supported" does not
      mean, and the unpromoted extensions with the maintainer's reasons.
- [x] 4.2 The three READMEs become product pages: what the extension does for a user, install,
      configuration (settings, defaults, examples), support surface; the API-proof material stays in
      `docs/extensions/contributions/*` and `docs/spec/extensibility.md`.
- [x] 4.3 Docs that referenced the moved paths are re-pointed (`docs/extensions/contributions/*/`'s
      subagents, lsp-diagnostics and permission-gate links, `docs/extensions/testing.md`,
      `docs/extensions/packages.md`, `docs/extensions-remote.md`, `docs/spec/extensibility.md` — the
      fitness-suite section keeps the samples'capability rows and moves the three promoted ones onto
      the tier).

## 5. Spec and close-out

- [x] 5.1 `openspec validate first-party-extensions --strict` stays green; the docs gate passes
      (markdownlint, `scripts/docs-sync --check`).
- [x] 5.2 Narrow gate green: `dotnet build lunate.slnx -c Release`, `dotnet test` over the three moved
      test projects at their new paths, `dotnet csharpier check .`; the full matrix stays with CI.
- [ ] 5.3 Archive waits for review (the shepherd's merge), then `openspec archive
      first-party-extensions --yes` is the implementation PR's final commit.
