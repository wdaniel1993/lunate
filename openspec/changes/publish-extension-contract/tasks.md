# Tasks — publish the extension contract

## Group 1 — package the four projects (src + tests)

- [ ] `Lunate.Contracts` + `Lunate.Extensibility.Abstractions`: packable stable metadata (id,
      licence expression, icon, readme, description, repo URL), version bound to `ExtensionApi.Current`.
- [ ] `Lunate.Agent` + `Lunate.Extensibility.Testing`: `0.2.0-preview.1`, preview wording in
      description, `PrivateAssets="All"` on references to unpublished projects, `PackOwnAssemblies`
      target filling `lib/net10.0`, per-package READMEs ("no API stability promise before 1.0" in
      the README and description for the preview pair; stable READMEs carry the versioning and
      deprecation policy wording from design.md).
- [ ] Tests: version parity (Abstractions csproj version, Contracts csproj version,
      `ExtensionApi.Current`); each packable project yields a nupkg whose metadata passes
      `check-package-metadata.sh` fixtures rules (pack-time test in `Lunate.Extensibility.Tests` or
      a new test class in the touching test projects) with preview checks for the preview pair; the
      `lib` inventory of Agent and Testing (the explicit file list).
- [ ] Gate: `dotnet build` of the four projects, then the tests above.

## Group 2 — the gate and the release workflow

- [ ] `scripts/check-package-metadata.sh`: icon declared + packed at root; `--prerelease` flag;
      self-test fixture for each.
- [ ] `scripts/verify.sh`: new step packs all four SDK packages to a folder feed, runs the metadata
      check per package (preview flag on the preview pair), then builds the template with NuGet
      references forced against that feed; attach release workflow covers the new nupkgs.
- [ ] `.github/workflows/release.yml`: pack the four SDK packages + reference-extension path fix
      (`samples/extensions/subagents` → `extensions/subagents`), metadata check per package before
      push, the existing push/artifact/release steps cover the full `dist` set.
- [ ] Gate: the verify.sh sections touched + csharpier check.

## Group 3 — the template installs from NuGet

- [ ] `templates/extension/TemplateExtension.csproj`: dual-shape contract references
      (Exists() project reference in-repo / pinned PackageReference outside), attributes
      documented; snippet keeps markers for docs-sync.
- [ ] Docs: `docs/extensions/README.md` (snippet + the "not published as a NuGet package" sentence),
      `docs/extensions/testing.md` (kit reference guidance), `docs/distribution.md` asset table.

## Group 4 — ship it

- [ ] Full gate (`scripts/verify.sh` runs in CI; locally the narrow gate: build, touched test
      projects, csharpier, docs lint if docs touched).
- [ ] PR body current; acceptance checklist at the bottom of the PR (packages on the same tag via
      OIDC, template on NuGet, preview metadata, metadata gate green).
