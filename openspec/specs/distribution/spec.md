# distribution Specification

## Purpose

How Lunate reaches users: one-command installs (`install.sh` / `install.ps1`) that fetch checksum-verified release archives, the Homebrew/Scoop/winget artifacts and the `dotnet tool` package, and the runbook that drives the live distribution channels.

## Requirements

### Requirement: Install script (sh)

The repository SHALL provide `install.sh` for macOS and Linux: it detects the platform (`Darwin`+`arm64` → `osx-arm64`, `Linux`+`x86_64` → `linux-x64`), downloads the matching release archive and `SHA256SUMS` (latest, or the tag given via `--version` / `LUNATE_INSTALL_VERSION`), verifies the archive's SHA-256 before extracting, installs the `lunate` binary into `$HOME/.local/bin` (or `--prefix` / `LUNATE_INSTALL_PREFIX`), and prints the installed version plus a PATH hint when the directory is not on `PATH`. `LUNATE_INSTALL_BASE_URL` SHALL override the releases base (`https://github.com/wdaniel1993/lunate/releases` by default) and the script SHALL compose `latest/download/<asset>` or `download/<tag>/<asset>` from it (the test seam; `file://` URLs SHALL work). Unsupported platforms SHALL be refused with a message pointing Windows users at `install.ps1`, and the script SHALL never require `sudo` or leave a partial install behind.

#### Scenario: A supported platform installs from a release
- **GIVEN** a release containing the platform's archive and `SHA256SUMS`
- **WHEN** `install.sh` runs against it
- **THEN** the checksum is verified, `lunate` is installed into the target directory, is executable, and the installed version is printed

#### Scenario: A checksum mismatch aborts
- **GIVEN** an archive whose bytes do not match `SHA256SUMS`
- **WHEN** `install.sh` runs
- **THEN** it exits non-zero with a clear error and installs nothing

#### Scenario: An unsupported platform is refused
- **GIVEN** a platform outside the map (for example macOS on x86_64, or a non-Unix shell)
- **WHEN** `install.sh` runs
- **THEN** it exits non-zero naming the platform, and Windows is pointed at `install.ps1`

#### Scenario: Version and prefix flags are honoured
- **GIVEN** `--version` and `--prefix` arguments
- **WHEN** `install.sh` runs
- **THEN** the download composes that tag into the URL and the binary lands in that directory

### Requirement: Install script (PowerShell)

The repository SHALL provide `install.ps1` for Windows: on x86_64 it downloads `lunate-win-x64.zip` and `SHA256SUMS` (same seams as `install.sh`, settable via environment variables so `irm | iex` works without parameters), verifies the SHA-256 with `Get-FileHash`, expands into `%LOCALAPPDATA%\Programs\lunate`, adds that directory to the user `PATH` when missing, and prints the installed version plus a restart-shell hint. Unsupported architectures SHALL be refused, and it SHALL make no machine-wide changes.

#### Scenario: Windows installs from a release
- **GIVEN** a release containing the zip and `SHA256SUMS`
- **WHEN** `install.ps1` runs
- **THEN** the checksum is verified, `lunate.exe` is installed, runs, and the user `PATH` contains the install directory

#### Scenario: A checksum mismatch aborts
- **GIVEN** a zip whose bytes do not match `SHA256SUMS`
- **WHEN** `install.ps1` runs
- **THEN** it exits non-zero with a clear error and installs nothing

### Requirement: Channel artifacts

The repository SHALL carry the distribution-channel artifacts next to the code: a Homebrew formula (`packaging/homebrew/lunate.rb`) with macOS arm64 and Linux x64 release URLs, a Scoop manifest (`packaging/scoop/lunate.json`) with `checkver`/`autoupdate`, and winget manifests (`packaging/winget/`) for a portable x64 install. Each artifact SHALL name the release asset filenames and the current `<Version>` from `Directory.Build.props`, with SHA-256 placeholders at rest that the release runbook fills. `docs/distribution.md` SHALL document the live-channel runbook (tap and bucket repositories, winget submission including the pre-release name check, NuGet push, first-release checklist).

#### Scenario: Packaging files carry the current version
- **GIVEN** the packaging files and `Directory.Build.props`
- **WHEN** the verify gate runs
- **THEN** every packaging file exists and contains the current version string

### Requirement: Dotnet tool package

`Lunate.Coding` SHALL pack as a framework-dependent .NET tool with package id `lunate` and command `lunate`, adding no new dependencies; the release workflow SHALL attach the `.nupkg` to the GitHub release; and the verify gate SHALL prove a local roundtrip — pack, `dotnet tool install` from the local feed, run `lunate --version`.

#### Scenario: The tool roundtrips from a local feed
- **GIVEN** a `dotnet pack` of the CLI and an empty tool path
- **WHEN** the tool is installed from the local feed and run
- **THEN** `lunate --version` reports the current version

### Requirement: Extension package

The release SHALL publish one extension as a real NuGet package beside the tool: `Lunate.Extensions.Subagents`, the reference subagents extension under `samples/extensions/subagents`, packed as a bundle — `lunate.package.json` at the package root and the extension's files under `extension/` — at the repository's `<Version>`, with no host assembly inside it. The same `v*` tag that publishes the tool SHALL push the package to nuget.org through the existing Trusted Publishing policy, and SHALL attach it to the GitHub release. The verify gate SHALL pack the sample and prove install, `lunate packages --verify` and uninstall from a folder feed with the published binary; the release smoke SHALL prove the same cycle against the published feed.

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

- **GIVEN** a change to the sample's build output or packaging
- **WHEN** the verify gate runs
- **THEN** the packed nupkg installs from a folder feed, `lunate packages --verify` reports it verified, and the uninstall leaves no content and no record

### Requirement: Release smoke

The repository SHALL provide a `smoke.yml` workflow, dispatched with a release tag, that verifies that tag's *published* artifacts on ubuntu, macos and windows: both documented installers install the archive, the installed binary reports that version and starts, and the extension package the same tag published is installed by id from the feed a user reads, listed by `lunate packages`, verified with `--verify`, discovered by `lunate extensions` as pending trust, and uninstalled with neither content nor record left behind. The workflow SHALL keep the install in a state root outside the checkout, SHALL run keyless, and SHALL fail by name when the tag published no such package version. Before it installs the package, the workflow SHALL wait for the feed to list that version — polling the flatcontainer index, the package content and the registration resource under one budget of about ten minutes — and SHALL install once, against a feed it has just seen serve the version. When the budget runs out it SHALL fail naming the version it waited for and the state of each resource, rather than retrying a fixed number of times.

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

#### Scenario: A freshly pushed version is waited for, not retried

- **GIVEN** a tag whose package was pushed minutes ago and is not yet indexed by the feed
- **WHEN** the smoke reaches the extension step
- **THEN** it waits until the feed lists the version in the flatcontainer index, serves its `.nupkg`, and lists it in the registration resource, and then installs it once

#### Scenario: An exhausted budget names the feed state

- **GIVEN** a tag whose package version does not become visible within the budget
- **WHEN** the wait ends
- **THEN** the step fails naming the package and version and what each resource served — absent from the index, content not downloadable, registration not served, or unreachable — and the install is not attempted

#### Scenario: The wait is proven without the network

- **GIVEN** fixture feeds served from the filesystem
- **WHEN** the gate's self-tests run
- **THEN** the wait succeeds for a version that appears in the middle of the budget, fails when the version never appears, and refuses a version whose content is missing
