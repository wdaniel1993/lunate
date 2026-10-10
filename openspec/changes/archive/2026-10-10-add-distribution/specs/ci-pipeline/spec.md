## MODIFIED Requirements

### Requirement: Release artifacts

On `v*` tags, CI SHALL run the full verification gate before publishing and SHALL fail when the tag does not match the version in `Directory.Build.props`; it SHALL publish self-contained single-file binaries for osx-arm64, win-x64 and linux-x64, attach one archive per target to the GitHub release, attach the framework-dependent `dotnet tool` package, and attach `SHA256SUMS` covering the archives.

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

#### Scenario: The tool package ships with the release
- **GIVEN** a `v*` tag that passes the gate
- **WHEN** the release completes
- **THEN** the `lunate` `.nupkg` is attached next to the archives
