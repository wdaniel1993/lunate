## 1. Tooling foundation (CSharpier)

- [x] 1.1 `.config/dotnet-tools.json` (csharpier pinned 1.3.0; created via `dotnet new tool-manifest` + `dotnet tool install csharpier --version 1.3.0`), `.csharpierrc.yaml` (printWidth 100, indentSize 4, useTabs false, endOfLine lf), `.csharpierignore` (bin/, obj/, artifacts/), `.gitignore` (+ `.csharpier/` cache entry)
- [x] 1.2 `.editorconfig`: `dotnet_diagnostic.IDE0055.severity = none` with a comment (CSharpier owns formatting)
- [x] 1.3 One-time reformat: `dotnet tool restore && dotnet csharpier format .` committed alone as `style: apply CSharpier formatting (one-time)`; then `.git-blame-ignore-revs` with that commit hash (commented) committed separately; verify still green
- [x] 1.4 `scripts/verify.sh`: add `dotnet tool restore` step; format step becomes `dotnet csharpier check .` + `dotnet format style lunate.sln --verify-no-changes --no-restore` + `dotnet format analyzers lunate.sln --verify-no-changes --no-restore`; actionable error when the tool is missing
- [x] 1.5 `scripts/verify.ps1`: same changes as 1.4
- [x] 1.6 `scripts/gate-tests.sh`: formatting self-test (unformatted temp file fails `dotnet csharpier check`; formatted passes)
- [x] 1.7 verify green on the full path (bash locally; PowerShell verified by Windows CI)

## 2. Documentation lint

- [x] 2.1 `.markdownlint-cli2.yaml` (globs README.md, AGENTS.md, docs/**/*.md, adr/**/*.md; MD013 off; further disables only with a comment)
- [x] 2.2 Fix violations in scope (docs-only edits)
- [x] 2.3 `scripts/verify.sh` + `scripts/verify.ps1`: `npx --yes markdownlint-cli2@0.23.3` step; actionable error when Node is missing

## 3. Contribution templates

- [x] 3.1 `.github/pull_request_template.md` (What this is / What lands / Review map / Verification / Notes)
- [x] 3.2 `.github/ISSUE_TEMPLATE/bug_report.yml` + `feature_request.yml`

## 4. Code scanning

- [x] 4.1 `.github/workflows/codeql.yml` (csharp; build-mode manual: dotnet restore + build; security-extended; push/PR main + weekly cron; security-events permission; fork guard)
- [ ] 4.2 Confirm a green CodeQL run on the change PR; triage any alerts as follow-ups (security-relevant ones are blockers)

## 5. Documentation entry points

- [x] 5.1 Rewrite `README.md` (what it is, status, quick start with prerequisites — .NET 10 SDK, Node.js for the docs lint —, repository map, links: docs/README.md, guide, specs, ADRs, releases; MIT)
- [x] 5.2 New `docs/README.md` (documentation map + writing guideline per design.md; note `.git-blame-ignore-revs` usage)
- [x] 5.3 `docs/guide.md`: short Repository-quality note (CSharpier, markdownlint, CodeQL, docs map; pointer to ADR-0011)
- [x] 5.4 `AGENTS.md` (PROTECTED — maintainer present; exact text in design.md; if a permission prompt appears, stop and wait)
- [x] 5.5 `adr/0011-repo-quality-tooling.md` (status proposed; content per adr.md in this change)

## 6. Close

- [x] 6.1 `scripts/verify.sh` green; gate self-tests green; `openspec validate add-repo-quality-tooling --type change --strict`
- [x] 6.2 Self-review pass; fix findings; commit per group (do NOT spawn subagents; the adversarial review is a separate dispatch)
