## ADDED Requirements

### Requirement: Extension package

The release SHALL publish one extension as a real NuGet package beside the tool: `Lunate.Samples.Subagents`, the reference subagents extension under `samples/extensions/subagents`, packed as a bundle — `lunate.package.json` at the package root and the extension's files under `extension/` — at the repository's `<Version>`, with no host assembly inside it. The same `v*` tag that publishes the tool SHALL push the package to nuget.org through the existing Trusted Publishing policy, and SHALL attach it to the GitHub release. The verify gate SHALL pack the sample and prove install, `lunate packages --verify` and uninstall from a folder feed with the published binary; the release smoke SHALL prove the same cycle against the published feed.

#### Scenario: The tag publishes the package

- **GIVEN** a `v*` tag that matches `<Version>` in `Directory.Build.props`
- **WHEN** the release workflow completes
- **THEN** the GitHub release carries `Lunate.Samples.Subagents.<version>.nupkg`, `SHA256SUMS` covers it, and nuget.org serves that exact version

#### Scenario: A user installs the reference extension

- **GIVEN** a published `Lunate.Samples.Subagents`
- **WHEN** `lunate install Lunate.Samples.Subagents@<version>` runs against a feed that serves it
- **THEN** the extension is installed, listed by `lunate packages`, verifies with `--verify`, and `lunate uninstall` removes exactly what the install wrote

#### Scenario: A dry run publishes nothing

- **GIVEN** a manual workflow dispatch instead of a tag
- **WHEN** the release workflow runs
- **THEN** both packages are packed and uploaded as workflow artifacts, and neither is pushed to nuget.org

#### Scenario: The bundle layout is gated

- **GIVEN** a change to the sample's build output or packaging
- **WHEN** the verify gate runs
- **THEN** the packed nupkg installs from a folder feed, `lunate packages --verify` reports it verified, and the uninstall leaves no content and no record

### Requirement: Release smoke

The repository SHALL provide a `smoke.yml` workflow, dispatched with a release tag, that verifies that tag's *published* artifacts on ubuntu, macos and windows: both documented installers install the archive, the installed binary reports that version and starts, and the extension package the same tag published is installed by id from the feed a user reads, listed by `lunate packages`, verified with `--verify`, discovered by `lunate extensions` as pending trust, and uninstalled with neither content nor record left behind. The workflow SHALL keep the install in a state root outside the checkout, SHALL run keyless, and SHALL fail by name when the tag published no such package version.

#### Scenario: A published tag passes the smoke

- **GIVEN** a release tag whose archive and extension package were published
- **WHEN** `smoke.yml` runs for that tag
- **THEN** all three operating systems install the archive, run the binary, and complete install → verify → uninstall of the package

#### Scenario: A tag without the package fails by name

- **GIVEN** a release tag cut before the extension package existed
- **WHEN** `smoke.yml` runs for that tag
- **THEN** the package step fails naming the package and the version it asked the feed for

#### Scenario: The smoke cannot be made to pass by a local store

- **GIVEN** a runner whose home already holds a Lunate store
- **WHEN** `smoke.yml` runs
- **THEN** every package command reads and writes a state root under the runner's temporary directory, and the pre-existing store is neither read nor written
