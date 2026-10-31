# Tasks — publish the extension contract

## Group 1 — package the four projects (src + tests)

- [x] `Lunate.Contracts` + `Lunate.Extensibility.Abstractions`: packable stable metadata (id,
      licence expression, icon, readme, description, repo URL), version bound to `ExtensionApi.Current`.
- [x] `Lunate.Agent` + `Lunate.Extensibility.Testing`: `0.2.0-preview.1`, preview wording in
      description, `PrivateAssets="All"` on references to unpublished projects, `PackOwnAssemblies`
      target filling `lib/net10.0`, per-package READMEs ("no API stability promise before 1.0" in
      the README and description for the preview pair; stable READMEs carry the versioning and
      deprecation policy wording from design.md).
- [x] Tests: version parity (Abstractions csproj version, Contracts csproj version,
      `ExtensionApi.Current`); each packable project yields a nupkg whose metadata passes
      `check-package-metadata.sh` fixtures rules (pack-time test in `Lunate.Extensibility.Tests`
      or a new test class in the touching test projects) with preview checks for the preview pair;
      the `lib` inventory of Agent and Testing (the explicit file list).
      → `tests/Lunate.Extensibility.Tests/ExtensionContractPackages.cs` (6 tests) + the assembly-
      version parity tests that already exist in `ExtensionApiTests`; the packed-nupkg proof runs
      in the gate (see Group 2), which a test process must not build.
- [x] Gate: `dotnet build` of the four projects, then the tests above.

## Group 2 — the gate and the release workflow

- [x] `scripts/check-package-metadata.sh`: icon declared + packed at root; `--prerelease` flag;
      self-test fixture for each (12 cases green).
- [x] `scripts/verify.sh`: new step packs all four SDK packages to a folder feed, runs the metadata
      check per package (preview flag on the preview pair), then builds the template with NuGet
      references forced against that feed; the release workflow's push/attach steps cover the new
      nupkgs because they already push and attach `dist/*.nupkg`.
- [x] `.github/workflows/release.yml`: pack the four SDK packages + the reference-extension path
      fix (`samples/extensions/subagents` → `extensions/subagents`; the deployable path did not
      exist — see the PR comment), metadata check per package before push, the existing
      push/artifact/release steps cover the full `dist` set.
- [x] Gate: the verify.sh sections touched (pack, checks, template build all run green locally) +
      csharpier check.

## Group 3 — the template installs from NuGet

- [x] `templates/extension/TemplateExtension.csproj` and its test project: dual-shape contract
      references (`LunateUseNuGetContracts`: project references in-repo / pinned PackageReference
      outside), attributes documented; snippet keeps markers for docs-sync; the gate forces the
      package shape against a folder feed.
- [x] Docs: `docs/extensions/README.md` (snippet + the "not published as a NuGet package" sentence),
      `docs/extensions/testing.md` (the kit's package form), `docs/distribution.md` asset table.

## Group 4 — ship it

- [x] Full gate runs in CI; local narrow gate green: solution Release build, every touched test
      project (Extensibility 735, Coding 2321, kit consumers, template), csharpier, docs lint,
      docs-sync.
- [ ] PR body current; acceptance checklist at the bottom of the PR (packages on the same tag via
      OIDC, template on NuGet, preview metadata, metadata gate green).
