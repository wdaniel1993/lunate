## ADDED Requirements

### Requirement: The SDK packages publish on the same tag

The release SHALL publish the extension contract beside the tool on the same `v*` tag through the
Trusted Publishing path the tool uses: `Lunate.Contracts` and `Lunate.Extensibility.Abstractions` as
**stable** packages, and `Lunate.Agent` with `Lunate.Extensibility.Testing` as **preview** packages.
The stable packages SHALL carry the version `ExtensionApi.Current` names — `1.15.0` at publication,
not the tool's version — and the preview pair SHALL be `0.2.0-preview.<n>` with "no API stability
promise before 1.0" in the package description and README. Every nupkg the release pushes SHALL pass
`scripts/check-package-metadata.sh` — licence expression, packed readme, icon and git repository URL
— and the preview pair SHALL pass it under a prerelease flag, in the gate and before any push.

#### Scenario: The tag publishes the contract set
- **GIVEN** a `v*` tag that matches `<Version>` in `Directory.Build.props`
- **WHEN** the release workflow completes
- **THEN** nuget.org serves `Lunate.Contracts.<ExtensionApi.Current>`,
  `Lunate.Extensibility.Abstractions.<ExtensionApi.Current>`, `Lunate.Agent.0.2.0-preview.<n>` and
  `Lunate.Extensibility.Testing.0.2.0-preview.<n>`, and the GitHub release carries all of them with
  `SHA256SUMS` coverage

#### Scenario: A preview package says preview
- **GIVEN** a packed `Lunate.Agent` or `Lunate.Extensibility.Testing`
- **WHEN** `check-package-metadata.sh` runs on it with the prerelease flag
- **THEN** the check passes only because the version carries the preview suffix; feeding a stable
  package to the same check fails naming the requirement

#### Scenario: A dry run uploads everything and publishes nothing
- **GIVEN** a manual workflow dispatch instead of a tag
- **WHEN** the release workflow runs
- **THEN** every package — the tool, the reference extension and the four SDK packages — is packed,
  metadata-checked and uploaded as workflow artifacts, and none is pushed to nuget.org

### Requirement: The template installs the contract from NuGet

The packaged-extension template's contract references SHALL be package references pinned to the
version `ExtensionApi.Current` names, compile-only as the host requires
(`PrivateAssets="all"`, `ExcludeAssets="runtime"`). A template that sits inside this repository MAY
keep project references while the packages it needs are not on the machine's feed; the verify gate
SHALL prove the NuGet shape by binding the template to a folder feed of freshly packed packages and
building the template that way.

#### Scenario: A template copy outside the repository compiles against NuGet
- **GIVEN** the template's csproj copied into a project whose checkout does not sit inside this
  repository
- **WHEN** the project restores and builds
- **THEN** the contract assemblies come from the NuGet feed pinned at the ExtensionApi version and
  the two contract dll names are absent from the extension's output directory

#### Scenario: The gate proves the package path without the packages on nuget.org
- **GIVEN** a fresh checkout of this repository
- **WHEN** the verify gate runs
- **THEN** the four SDK packages are packed into a folder feed and the template is built with NuGet
  references bound to that feed, or the gate fails if packaging or the template drift apart
