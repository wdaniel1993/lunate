# Delta — first-party extensions: the reference package follows the source move

## MODIFIED Requirements

### Requirement: Extension package

The release SHALL publish one extension as a real NuGet package beside the tool: `Lunate.Extensions.Subagents`, the reference subagents extension under `extensions/subagents`, packed as a bundle — `lunate.package.json` at the package root and the extension's files under `extension/` — at the repository's `<Version>`, with no host assembly inside it. The same `v*` tag that publishes the tool SHALL push the package to nuget.org through the existing Trusted Publishing policy, and SHALL attach it to the GitHub release. The verify gate SHALL pack the supported extension and prove install, `lunate packages --verify` and uninstall from a folder feed with the published binary; the release smoke SHALL prove the same cycle against the published feed.

#### Scenario: The tag publishes the package

- **GIVEN** a `v*` tag that matches `<Version>` in `Directory.Build.props`
- **WHEN** the release workflow completes
- **THEN** the GitHub release carries `Lunate.Extensions.Subagents.<version>.nupkg`, `SHA256SUMS` covers it, and nuget.org serves that exact version

#### Scenario: A user installs the reference extension

- **GIVEN** a published `Lunate.Extensions.Subagents`
- **WHEN** `lunate install Lunate.Extensions.Subagents@<version>` runs against a feed that serves it
- **THEN** the extension is installed, listed by `lunate packages`, verifies with `--verify`, and `lunate uninstall` removes exactly what the install wrote

#### Scenario: A dry run publishes nothing

- **GIVEN** a manual workflow dispatch instead of a tag
- **WHEN** the release workflow runs
- **THEN** both packages are packed and uploaded as workflow artifacts, and neither is pushed to nuget.org

#### Scenario: The bundle layout is gated

- **GIVEN** a change to the extension's build output or packaging
- **WHEN** the verify gate runs
- **THEN** the packed nupkg installs from a folder feed, `lunate packages --verify` reports it verified, and the uninstall leaves no content and no record
