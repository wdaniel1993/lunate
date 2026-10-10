## ADDED Requirements

### Requirement: Install script (sh)

The repository SHALL provide `install.sh` for macOS and Linux: it detects the platform (`Darwin`+`arm64` → `osx-arm64`, `Linux`+`x86_64` → `linux-x64`), downloads the matching release archive and `SHA256SUMS` (latest, or the tag given via `--version` / `LUNATE_INSTALL_VERSION`), verifies the archive's SHA-256 before extracting, installs the `lunate` binary into `$HOME/.local/bin` (or `--prefix` / `LUNATE_INSTALL_PREFIX`), and prints the installed version plus a PATH hint when the directory is not on `PATH`. `LUNATE_INSTALL_BASE_URL` SHALL override the download root verbatim (the test seam; `file://` URLs SHALL work). Unsupported platforms SHALL be refused with a message pointing Windows users at `install.ps1`, and the script SHALL never require `sudo` or leave a partial install behind.

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
- **THEN** the download uses that tag and the binary lands in that directory

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
