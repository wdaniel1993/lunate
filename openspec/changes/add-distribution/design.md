# Design: distribution — install scripts, channels, dotnet tool (T-32)

## Structure

- `install.sh` (repo root) — macOS/Linux installer; `install.ps1` (repo root) — Windows installer.
- `packaging/homebrew/lunate.rb`, `packaging/scoop/lunate.json`, `packaging/winget/{wdaniel1993.lunate.yaml, wdaniel1993.lunate.locale.en-US.yaml, wdaniel1993.lunate.installer.yaml}`.
- `docs/distribution.md` — channel runbook; README gains the install section.
- `src/Lunate.Coding/Lunate.Coding.csproj` — tool packaging; `.github/workflows/release.yml` — pack job + `.nupkg` attachment.
- `scripts/verify.sh` / `scripts/verify.ps1` — install-script and tool-roundtrip steps.

## Asset contract (pinned)

- Release asset names: `lunate-osx-arm64.tar.gz`, `lunate-linux-x64.tar.gz`, `lunate-win-x64.zip`, `SHA256SUMS` (lines `<hash>  <filename>`, sha256sum format), `lunate.<version>.nupkg` (new).
- Download URLs: latest = `https://github.com/wdaniel1993/lunate/releases/latest/download/<asset>`; pinned version = `.../releases/download/<tag>/<asset>`.
- Test seam: `LUNATE_INSTALL_BASE_URL` overrides the **releases base** (`https://github.com/wdaniel1993/lunate/releases`; `file://` works with curl), and the script composes `/latest/download/<asset>` or `/download/<tag>/<asset>` from it; `LUNATE_INSTALL_VERSION` / `--version` selects the tag; `LUNATE_INSTALL_PREFIX` / `--prefix` the target directory.

## install.sh (pinned)

- Platform map: `Darwin`+`arm64` → `osx-arm64`; `Linux`+`x86_64` → `linux-x64`; anything else → refuse with a message naming the platform and pointing Windows users at `install.ps1`. No `sudo`, ever.
- Flow: resolve base URL (env > default) → download `<base>/lunate-<rid>.tar.gz` and `<base>/SHA256SUMS` with `curl -fsSL` → extract the expected hash line → compute sha256 (`sha256sum` when present, else `shasum -a 256`) → abort (installing nothing) on mismatch → extract to a temp dir → install `lunate` into `$HOME/.local/bin` (or `--prefix`/env) → `chmod +x` → print installed version and a PATH hint when the directory is not on `PATH`.
- Flags: `--version <tag>`, `--prefix <dir>`, `--help`; everything else = usage error.
- Errors are plain English on stderr with exit 1; no partial installs (temp dir cleanup).

## install.ps1 (pinned)

- win-x64 only (`PROCESSOR_ARCHITEW6432` when set — 32-bit PowerShell on x64 — else `PROCESSOR_ARCHITECTURE`); other architectures refused with a clear message.
- Flow: same base/version seams (env first, `param()` when run as a file; iex-friendly defaults) → download `lunate-win-x64.zip` + `SHA256SUMS` → `Get-FileHash -Algorithm SHA256` vs the parsed line → `Expand-Archive` to a temp dir → copy `lunate.exe` into `$env:LOCALAPPDATA\Programs\lunate` → add that directory to the **user** PATH when missing (comparisons normalize `/`, `\` and trailing separators) → print version + restart-shell hint.
- No machine-wide changes; no admin.

## Verify-gate proofs (pinned)

- `verify.sh`, after the startup-budget step, on Darwin/Linux: build a fixture release from the just-published binary (`tar -czf lunate-<rid>.tar.gz`, `SHA256SUMS`) under `<fixture>/latest/download/` and `<fixture>/download/<tag>/`; run `install.sh` (a) with only `LUNATE_INSTALL_BASE_URL=file://<fixture>` and a temp prefix, then (b) with `--version <tag> --prefix <dir2>`, and compare each installed `lunate --version` with the publish version; a nonexistent tag must fail (the tag participates in the URL).
- `verify.sh` on Windows (MINGW): run `install.sh` and require the refusal (non-zero + message) — the Windows path belongs to `install.ps1`.
- `verify.ps1`, after the startup-budget step: the same fixture layout and (a)/(b) runs with `install.ps1` (flags via `-Version`/`-Prefix`), plus the checksum-mismatch abort, the ARM64 refusal, the WOW64 acceptance (`PROCESSOR_ARCHITEW6432=AMD64` with `PROCESSOR_ARCHITECTURE=x86` installs) and a PATH no-duplicate assertion after a trailing-separator prefix.
- Tool roundtrip (both scripts): `dotnet pack` the CLI into a temp feed, `dotnet tool install --tool-path <tmp> --add-source <feed> lunate --version <current>`, run `lunate --version`.
- Packaging consistency: assert every packaging file exists and contains the current `<Version>` string from `Directory.Build.props` (a small bash step in verify.sh; sha256 placeholders are allowed at rest — the runbook fills them at release time).

## Tool packaging (pinned)

- `Lunate.Coding.csproj`: `PackAsTool=true`, `ToolCommandName=lunate`, `PackageId=lunate`, description; framework-dependent (default). No dependency changes.
- `release.yml`: new `pack` job (needs gate) producing the `.nupkg`; the release job downloads it with the archives and attaches it (and includes it in the upload glob).

## Channel artifacts (pinned)

- Homebrew: formula with `on_macos`/`on_linux` blocks using the release URLs; `sha256` placeholders (`SHA256_PLACEHOLDER_*`) at rest; the tap repo `wdaniel1993/homebrew-tap` hosts the copy (runbook step).
- Scoop: manifest with `checkver`/`autoupdate` against GitHub releases; bucket repo `wdaniel1993/scoop-bucket` (runbook step).
- winget: three manifests (portable, x64) with `InstallerSha256` placeholder; submission to `microsoft/winget-pkgs` via PR (runbook step; the guide's pre-release name check lives there).
- Runbook (`docs/distribution.md`): first-release checklist (tag → verify assets → fill placeholders → update tap/bucket → winget PR → NuGet push), each step with the exact command.

## Deviations

1. The installers are served from the repository (`raw.githubusercontent.com/wdaniel1993/lunate/main/...`), not attached to the GitHub release: the pinned asset contract lists only the three archives, `SHA256SUMS` and the `.nupkg`. README and runbook use the raw URLs.
2. `Lunate.Coding.csproj` sets `<IsPackable>true</IsPackable>` next to `PackAsTool=true`: `Directory.Build.props` sets `IsPackable=false` repo-wide, and without the override `dotnet pack` no-ops instead of producing the tool package required by task 3.1.
3. `install.ps1` resolves `file://` base URLs through the local filesystem (copy): PowerShell 7's `Invoke-WebRequest` rejects the `file` scheme, and the verify fixture seam needs `file://`. http(s) downloads still go through `Invoke-WebRequest`.
4. `lunate --version` reports the SDK informational version (`<Version>+<commit>`), so the fixture flows compare the installed binary's output with the published binary's output, and the tool roundtrip compares against the `<Version>` prefix before `+`.
5. The test seam overrides the **releases base** (not the download URL): the scripts compose `/latest/download/<asset>` or `/download/<tag>/<asset>` from it, so `--version`/`LUNATE_INSTALL_VERSION` are provable end-to-end (adversarial review risk 1).
6. `install.ps1` resolves the architecture from `PROCESSOR_ARCHITEW6432` when set (32-bit PowerShell on x64 reports `x86` in `PROCESSOR_ARCHITECTURE`) and refuses only when neither variable names x64 (adversarial review risk 3a).
7. `install.ps1` normalizes `/`, `\` and trailing separators on both sides of the user-PATH comparison, so `C:\x\` or `C:/x/` match an existing `C:\x` entry — no duplicate entry and no false "already on PATH" hint (adversarial review risk 3b).

## Seams

- Live channel execution (first tag, tap/bucket repos, winget PR, NuGet push) is ops after merge — the change proves mechanics; the runbook drives the live part.
- Automating placeholder updates per release (tap/bucket bots) is future work, noted in the runbook.
