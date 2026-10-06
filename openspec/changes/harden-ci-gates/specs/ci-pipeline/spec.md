## MODIFIED Requirements

### Requirement: Per-commit verification matrix
CI SHALL build and test the solution on ubuntu, macos and windows runners for every push to `main` and every pull request, running the full verification gate on each OS. Pushes that touch only documentation MAY skip the matrix but SHALL still run the documentation lint.

#### Scenario: Green matrix on a clean push
- **GIVEN** a push that satisfies the gate
- **WHEN** the CI matrix runs
- **THEN** all three runner jobs complete green

#### Scenario: A failing gate turns CI red
- **GIVEN** a push whose tests fail
- **WHEN** the CI matrix runs
- **THEN** the affected runner job fails and the run is red

#### Scenario: Docs-only push still lints
- **GIVEN** a push to `main` that touches only documentation
- **WHEN** the docs-lint workflow runs
- **THEN** the documentation lint completes green, and a lint violation would fail the workflow

### Requirement: Release artifacts
On `v*` tags, CI SHALL run the full verification gate before publishing and SHALL fail when the tag does not match the version in `Directory.Build.props`; it SHALL publish self-contained single-file binaries for osx-arm64, win-x64 and linux-x64, attach one archive per target to the GitHub release, and attach `SHA256SUMS` covering the archives.

#### Scenario: Tag produces three artifacts
- **GIVEN** a `v*` tag
- **WHEN** the release workflow runs
- **THEN** the GitHub release carries one archive for each of the three target RIDs

#### Scenario: Dry run without a release
- **GIVEN** a manual workflow dispatch in dry-run mode
- **WHEN** the release workflow runs
- **THEN** the three target artifacts are produced and uploaded as workflow artifacts

#### Scenario: Tag and version mismatch fails before publishing
- **GIVEN** a tag whose version does not match `<Version>` in `Directory.Build.props`
- **WHEN** the release workflow runs
- **THEN** the gate job fails and nothing is published

#### Scenario: Archives ship with checksums
- **GIVEN** a `v*` tag that passes the gate
- **WHEN** the release completes
- **THEN** `SHA256SUMS` is attached next to the three archives and matches them

## ADDED Requirements

### Requirement: SDK pinning
The CI, CodeQL and release workflows SHALL install the .NET SDK version pinned in `global.json`.

#### Scenario: Workflows resolve the pinned SDK
- **GIVEN** a workflow run on any runner
- **WHEN** the SDK is set up
- **THEN** the installed version is the one pinned in `global.json`
