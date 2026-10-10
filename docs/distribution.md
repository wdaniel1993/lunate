# Distribution runbook

How Lunate reaches users: what a release carries, how the one-command installers work, and the exact steps that take each channel live. The mechanics are proven by the verify gate on all three CI runners (`scripts/verify.sh` and `scripts/verify.ps1` build a fixture release, install from it and run the binary); this document drives the ops part that runs once per release.

## Asset contract

| Asset | What it is |
| --- | --- |
| `lunate-osx-arm64.tar.gz` | macOS Apple Silicon single-file binary (`lunate` at the archive root) |
| `lunate-linux-x64.tar.gz` | Linux x86_64 binary |
| `lunate-win-x64.zip` | Windows x64 binary (`lunate.exe`) |
| `SHA256SUMS` | `<hash>  <filename>` lines covering the three archives |
| `lunate.<version>.nupkg` | framework-dependent `dotnet tool` (package and command `lunate`) |

- Installers: [`install.sh`](../install.sh) (`curl | sh`, macOS arm64 + Linux x64) and [`install.ps1`](../install.ps1) (`irm | iex`, Windows x64). Both verify SHA-256 before installing, never need `sudo`/admin, and are served from the repository (`raw.githubusercontent.com/wdaniel1993/lunate/main/...`), not from the release assets.
- Channel artifacts live in [`packaging/`](../packaging): Homebrew formula, Scoop manifest, winget manifests. Their `SHA256_PLACEHOLDER_*`/sha values are filled per release (step 3).
- Release asset names, the installer platform map and the `LUNATE_INSTALL_*` environment seams are pinned in the distribution spec (`openspec/specs/distribution/spec.md` after archive).

## Pre-release checks (manual, T-33)

Before tagging, run the terminal matrix from the guide: Windows Terminal, conhost, Git Bash, macOS Terminal, one Linux terminal, and tmux over SSH. The tag itself runs the full verify gate on ubuntu, macos and windows before anything is published.

## First-release checklist

The commands below use `0.1.0`; substitute the version from `Directory.Build.props` for later releases.

### 1. Tag the release

CI fails the tag when it does not match `<Version>` in `Directory.Build.props`.

```sh
git switch main
git pull
git tag v0.1.0
git push origin v0.1.0
```

### 2. Verify the release assets

Wait for the Release workflow, then check the release page carries the three archives, `SHA256SUMS` and the `.nupkg`, and that the checksums verify.

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

Create an API key on nuget.org (scope: push new packages and package versions, glob `lunate`) and push the package from the release.

```sh
gh release download v0.1.0 --pattern '*.nupkg' --dir /tmp/lunate-release
dotnet nuget push /tmp/lunate-release/lunate.0.1.0.nupkg \
  --source https://api.nuget.org/v3/index.json \
  --api-key <NUGET_API_KEY>
dotnet tool install --global lunate --version 0.1.0
lunate --version
```

## Later releases

- Tag the new version; the gate, publish, pack and attach jobs do the rest.
- Fill the three sha values again (step 3), update the tap copy (step 4) and push the new `.nupkg` (step 7).
- The Scoop bucket autoupdates itself; winget needs a new `wingetcreate update wdaniel1993.lunate --version <v> --urls <zip-url>` submission.
- Future work: automate the placeholder rewrite and the tap/bucket updates from the release workflow (a follow-up change); until then the steps above stay manual.
