## 1. Scripts

- [ ] 1.1 `lib.sh`: `check_shipped_api_unchanged` (merge-base chain: `origin/main` → `main` → `HEAD` with a stderr note; prints the diff and the base on breach; returns non-zero)
- [ ] 1.2 `verify.sh`: replace the inline public-API block with the function; `verify.ps1`: same comparison chain in PowerShell + add the provider-runtime-assets step (mirror of the bash step: `Anthropic.dll`, `OpenAI.dll`, `Microsoft.Extensions.AI.dll` under `src/Lunate.Coding/bin/<config>/net10.0/`)
- [ ] 1.3 `gate-tests.sh`: self-test for the Shipped check in a synthetic repository — breach direction (committed change on a branch vs `main`), clean direction, and the no-remote fallback; single EXIT trap covering both temp dirs

## 2. Workflows

- [ ] 2.1 `ci.yml`: `fetch-depth: 0` on the verify checkout (comment why), `global-json-file: global.json` on setup-dotnet, update the paths-ignore comment to mention the docs-lint workflow
- [ ] 2.2 New `docs-lint.yml`: push to `main`, paths filter (`*.md`, `docs/**`, `adr/**`, `openspec/**`), ubuntu, `npx --yes markdownlint-cli2@0.23.3`, concurrency group, contents: read
- [ ] 2.3 `codeql.yml`: `global-json-file: global.json`
- [ ] 2.4 `release.yml`: `gate` job (checkout `fetch-depth: 0`, setup-dotnet pinned, hyperfine Linux install, tag↔version check on tag pushes, `BUDGET_MS=250 bash scripts/verify.sh`); `publish` needs `gate`; release job writes `SHA256SUMS` (relative names) into `dist/` and uploads it with the archives; setup-dotnet pinned in all jobs

## 3. Tests

- [ ] 3.1 `LayeringChecker`: allowed runtime-package set for `Lunate.Agent` + violation finder; `LayeringTests`: reads `PackageReference`s from `Lunate.Agent.csproj`, exempts `PrivateAssets="all"`, asserts the runtime set equals the allowed set; `LayeringCheckerTests`: a violating package case

## 4. Roslyn and leftovers

- [ ] 4.1 `Lunate.Roslyn.csproj`: PublicApiAnalyzers package (PrivateAssets=all) + `AdditionalFiles`; `PublicAPI.Shipped.txt` / `PublicAPI.Unshipped.txt` with the `#nullable enable` header only
- [ ] 4.2 Delete `tests/Lunate.Agent.Tests/PlaceholderTests.cs`, `tests/Lunate.Coding.Tests/PlaceholderTests.cs` (their projects have real tests; keep the skeleton projects' placeholders) and `.github/workflows/s5-crash-probe.yml` (ADR-0008 settled)

## 5. Specs

- [ ] 5.1 `repo-foundation` delta: Enforced layering gains the `Lunate.Agent` package rule + scenario; Public API visibility pins the merge-base comparison base in requirement and scenarios
- [ ] 5.2 `ci-pipeline` delta: Per-commit verification matrix gains the docs-only clause + docs-lint scenario; Release artifacts gains gate + tag/version check + checksums + scenarios; new SDK pinning requirement + scenario

## 6. Close

- [ ] 6.1 `scripts/verify.sh` green and `scripts/gate-tests.sh` green locally; `verify.ps1` changes are exercised by the Windows CI runner (no local PowerShell available on macOS — state this in the report)
- [ ] 6.2 `openspec validate harden-ci-gates --type change --strict`; self-review pass; commit per group
