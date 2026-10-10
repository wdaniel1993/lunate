# Tasks: distribution — install scripts, channels, dotnet tool (T-32)

## 1. install.sh

- [x] 1.1 Platform map + refusal path (naming install.ps1) + usage/flags (`--version`, `--prefix`, `--help`)
- [x] 1.2 Download + SHA256SUMS parsing + checksum verify (sha256sum/shasum) + abort-on-mismatch + temp cleanup
- [x] 1.3 Install to prefix (default `$HOME/.local/bin`), chmod +x, version print, PATH hint
- [x] 1.4 Test fixture flow in `scripts/verify.sh` (install → `lunate --version`); Windows bash refusal assertion

## 2. install.ps1

- [ ] 2.1 win-x64 detection + refusal; env/param seams; download + Get-FileHash verify
- [ ] 2.2 Expand + install to `%LOCALAPPDATA%\Programs\lunate` + user PATH update + version/hint output
- [ ] 2.3 Test fixture flow in `scripts/verify.ps1` (install → run)

## 3. Tool packaging

- [ ] 3.1 `Lunate.Coding.csproj`: PackAsTool, ToolCommandName `lunate`, PackageId `lunate`
- [ ] 3.2 `release.yml` pack job + `.nupkg` attached to the release
- [ ] 3.3 Local pack → `dotnet tool install` → run roundtrip step in both verify scripts

## 4. Channel artifacts

- [ ] 4.1 `packaging/homebrew/lunate.rb` (macOS arm64 + Linux x64, sha placeholders)
- [ ] 4.2 `packaging/scoop/lunate.json` (checkver/autoupdate)
- [ ] 4.3 `packaging/winget/` three manifests (portable, x64, sha placeholder)
- [ ] 4.4 Version-consistency step in verify.sh (packaging files carry the current version)

## 5. Docs

- [ ] 5.1 README install section (curl | sh, irm | iex, brew, scoop, winget, dotnet tool; channels live at first release)
- [ ] 5.2 `docs/distribution.md` runbook (tap/bucket repos, winget submission + name check, NuGet push, first-release checklist)

## 6. Gate

- [ ] 6.1 `bash scripts/verify.sh` green (incl. the new steps); deviations recorded in design.md
