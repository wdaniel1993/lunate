# Distribution runbook

How Lunate reaches users: what a release carries, how the one-command installers work, and the exact steps that take each channel live. The mechanics are proven by the verify gate on all three CI runners (`scripts/verify.sh` and `scripts/verify.ps1` build a fixture release, install from it and run the binary; both also pack the extension package and install it from a folder feed, verify and uninstall it); this document drives the ops part that runs once per release.

## Asset contract

| Asset | What it is |
| --- | --- |
| `lunate-osx-arm64.tar.gz` | macOS Apple Silicon single-file binary (`lunate` at the archive root) |
| `lunate-linux-x64.tar.gz` | Linux x86_64 binary |
| `lunate-win-x64.zip` | Windows x64 binary (`lunate.exe`) |
| `lunate.<version>.nupkg` | framework-dependent `dotnet tool` (package and command `lunate`) |
| `Lunate.Samples.Subagents.<version>.nupkg` | the reference subagents extension, installed with `lunate install Lunate.Samples.Subagents` |
| `SHA256SUMS` | `<hash>  <filename>` lines covering every other asset |

- Installers: [`install.sh`](../install.sh) (`curl | sh`, macOS arm64 + Linux x64) and [`install.ps1`](../install.ps1) (`irm | iex`, Windows x64). Both verify SHA-256 before installing, never need `sudo`/admin, and are served from the repository (`raw.githubusercontent.com/wdaniel1993/lunate/main/...`), not from the release assets.
- Channel artifacts live in [`packaging/`](../packaging): Homebrew formula, Scoop manifest, winget manifests. Their `SHA256_PLACEHOLDER_*`/sha values are filled per release (step 3).
- The extension package is the distribution channel for extensions: the same tag that publishes the tool pushes it to nuget.org, so `lunate install <package-id>` installs the version the tag names. It is attached to the release as well, so a machine without feed access can point a `nuget.config` at the downloaded folder.
- Release asset names, the installer platform map and the `LUNATE_INSTALL_*` environment seams are pinned in the distribution spec (`openspec/specs/distribution/spec.md` after archive).

## Pre-release checks (manual, T-33)

Before tagging, run the terminal matrix from the guide: Windows Terminal, conhost, Git Bash, macOS Terminal, one Linux terminal, and tmux over SSH. The tag itself runs the full verify gate on ubuntu, macos and windows before anything is published.

## Release smoke

[`smoke.yml`](../.github/workflows/smoke.yml) verifies a *published* release rather than a branch, on ubuntu, macos and windows: both installers install the tag's archive, the binary prints that version and starts, and the extension package the same tag published is installed by id from nuget.org, listed by `lunate packages`, verified, discovered by `lunate extensions` (pending trust) and uninstalled with nothing left behind. Run it after the Release workflow finishes:

```sh
gh workflow run smoke.yml -f tag=v0.1.0
gh run list --workflow smoke.yml --limit 1
```

It is keyless by design: a session against a real model needs the maintainer's provider key, so that part of the smoke stays on a machine that has one. A tag cut before the extension package existed has no package to install; the step fails by name, which is the honest answer for that tag.

## First-release checklist

The commands below use `0.1.0`; substitute the version from `Directory.Build.props` for later releases.

Before the first tag, run the Release workflow once via `workflow_dispatch` (dry run) to exercise the pack job and the archives; the attach job only runs on tags, so nothing is published:

```sh
gh workflow run release.yml --ref main
gh run list --workflow release.yml --limit 1
```

### 1. Tag the release

CI fails the tag when it does not match `<Version>` in `Directory.Build.props`.

```sh
git switch main
git pull
git tag v0.1.0
git push origin v0.1.0
```

### 2. Verify the release assets

Wait for the Release workflow, then verify all six release assets are attached — the three archives, `SHA256SUMS`, the tool `.nupkg` and the extension `.nupkg` — and that the checksums verify.

```sh
gh release view v0.1.0
gh release download v0.1.0 --dir /tmp/lunate-release
cd /tmp/lunate-release
sha256sum -c SHA256SUMS   # macOS: shasum -a 256 -c SHA256SUMS
```

### 3. Fill the sha placeholders

Read the three archive hashes and replace the placeholders, then commit on `main`.

```sh
grep -E 'lunate-(osx-arm64|linux-x64|win-x64)' /tmp/lunate-release/SHA256SUMS
```

- `packaging/homebrew/lunate.rb`: `SHA256_PLACEHOLDER_OSX_ARM64`, `SHA256_PLACEHOLDER_LINUX_X64`
- `packaging/scoop/lunate.json`: `SHA256_PLACEHOLDER_WIN_X64`
- `packaging/winget/wdaniel1993.lunate.installer.yaml`: `InstallerSha256` (uppercase hex)

```sh
git add packaging
git commit -m "chore: fill 0.1.0 release hashes"
git push
```

### 4. Homebrew tap

The tap repo `wdaniel1993/homebrew-tap` hosts a copy of the formula (recreate it if it does not exist yet).

```sh
gh repo create wdaniel1993/homebrew-tap --public --description "Homebrew tap for Lunate"
gh repo clone wdaniel1993/homebrew-tap /tmp/homebrew-tap
mkdir -p /tmp/homebrew-tap/Formula
cp packaging/homebrew/lunate.rb /tmp/homebrew-tap/Formula/lunate.rb
cd /tmp/homebrew-tap
git add Formula/lunate.rb
git commit -m "lunate 0.1.0"
git push
brew tap wdaniel1993/tap
brew audit --formula wdaniel1993/tap/lunate
brew install wdaniel1993/tap/lunate
lunate --version
```

### 5. Scoop bucket

The bucket repo `wdaniel1993/scoop-bucket` hosts a copy of the manifest; `checkver`/`autoupdate` then track future releases automatically.

```sh
gh repo create wdaniel1993/scoop-bucket --public --description "Scoop bucket for Lunate"
gh repo clone wdaniel1993/scoop-bucket /tmp/scoop-bucket
mkdir -p /tmp/scoop-bucket/bucket
cp packaging/scoop/lunate.json /tmp/scoop-bucket/bucket/lunate.json
cd /tmp/scoop-bucket
git add bucket/lunate.json
git commit -m "Add lunate 0.1.0"
git push
scoop bucket add lunate https://github.com/wdaniel1993/scoop-bucket
scoop install lunate
```

### 6. winget

Check the identifier is free (pre-release name check; also look under `manifests/w/wdaniel1993/` in `microsoft/winget-pkgs`), then submit with WingetCreate, which validates and opens the PR.

```powershell
winget search wdaniel1993.lunate
winget install Microsoft.WingetCreate
wingetcreate new https://github.com/wdaniel1993/lunate/releases/download/v0.1.0/lunate-win-x64.zip
wingetcreate submit --token <github-token>
```

The `packaging/winget/` manifests are the reference for the answers (`Identifier: wdaniel1993.lunate`, portable nested `lunate.exe` with command alias `lunate`). `winget validate packaging\winget` checks them locally.

### 7. NuGet

Publishing is passwordless. A Trusted Publishing policy on nuget.org names
`wdaniel1993/lunate`, the `release.yml` workflow and the `production` environment; the
`pack` job asks GitHub for an OIDC token, trades it for a key valid for minutes, and pushes
both packages — the `lunate` tool and `Lunate.Samples.Subagents`. There is no API key in the
repository and nothing to rotate. The tag does it, so the only manual step is proving the
result:

```sh
dotnet tool install --global lunate --version 0.1.0
lunate --version
lunate install Lunate.Samples.Subagents@0.1.0 --dry-run
```

The `--dry-run` prints the identical identity the install would write — canonical id, exact
version, source and the nupkg's SHA-512 — without touching the store, so it proves the feed
serves the version the tag published without leaving an extension behind in the operator's
home. The full install is `smoke.yml`'s job (see *Release smoke* above), which runs it in a
throwaway state root on all three operating systems.

If the policy's repository, workflow file or environment ever stops matching the job, the
login step fails before anything is pushed — fix the policy on nuget.org rather than
reaching for an API key. A failed release is retried by re-running the *tag* run, which
carries the `push` event the publishing steps require:

```sh
gh run list --workflow release.yml --limit 5
gh run rerun <run-id>
```

`--skip-duplicate` makes the retry safe: a package version that already landed is a no-op,
and a version number is never reused for different bytes.

## Later releases

- Tag the new version; the gate, publish, pack and attach jobs do the rest.
- Fill the three sha values again (step 3) and update the tap copy (step 4); the tag pushes both `.nupkg` files itself (step 7).
- Run the release smoke against the tag (see *Release smoke*) before calling the release done.
- The Scoop bucket autoupdates itself; winget needs a new `wingetcreate update wdaniel1993.lunate --version <v> --urls <zip-url>` submission.
- Future work: automate the placeholder rewrite and the tap/bucket updates from the release workflow (a follow-up change); until then the steps above stay manual.
