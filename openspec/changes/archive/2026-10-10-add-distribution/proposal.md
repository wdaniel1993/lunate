# Proposal: distribution — install scripts, channels, dotnet tool (T-32)

## Why

The release workflow already produces the three single-file archives and attaches them with `SHA256SUMS` (ci-pipeline), but nothing installs them: there are no install scripts, no Homebrew/Scoop/winget artifacts, no `dotnet tool` package, and the README has no install story. The guide's done-when for T-32 is "fresh install works on Windows, macOS and Linux with one command" — this change builds every repo-side piece of that, with the mechanics proven in the verify gate on the real CI matrix, plus the runbook for the live channels.

## What changes

- **`install.sh`** (macOS/Linux, `curl | sh`): platform detection, download of the matching archive + `SHA256SUMS` from the GitHub release, checksum verification, install into `~/.local/bin` (or `--prefix`), PATH hint, no `sudo`; unsupported platforms refused with a pointer to `install.ps1`. `LUNATE_INSTALL_BASE_URL` is the test seam.
- **`install.ps1`** (Windows, `irm | iex`): win-x64 zip, checksum verification, install into `%LOCALAPPDATA%\Programs\lunate`, user-PATH update, same seams.
- **Verify-gate proofs**: both scripts are exercised end-to-end in `verify.sh` / `verify.ps1` against a fixture release built from the just-published binary (install → run `lunate --version`); the refusal path runs on Windows bash.
- **Channel artifacts**: `packaging/homebrew/lunate.rb`, `packaging/scoop/lunate.json`, `packaging/winget/` manifests, all version-consistent with `Directory.Build.props`; `docs/distribution.md` carries the runbook (tap/bucket repos, winget submission, NuGet push, first-release checklist).
- **`dotnet tool`**: `Lunate.Coding` packs as the framework-dependent tool `lunate`; the release workflow attaches the `.nupkg`; the verify gate proves a local pack → install → run roundtrip.
- **README**: one-command install section per platform.

## Done when

The scripts install and run the published binary from a fixture release on every CI runner; packaging files exist and carry the current version; the tool roundtrip passes; `scripts/verify.sh` green; the live channels (tap, bucket, winget, NuGet, first release) are executed from the runbook after merge.
