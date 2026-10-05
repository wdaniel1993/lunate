## Why

Every development session — human or AI — should start from documents that state what the product is, what is decided, and what the current state is, and should produce code whose style is not up for debate. Today the repo has the guide, the specs, the ADRs and the spikes, but no entry point that maps them, no documentation guideline, and no formatter: style is whatever the last editor or model produced. This change adds the missing quality tooling in one pass: a deterministic formatter (CSharpier), documentation linting (markdownlint), contribution templates, code scanning (CodeQL), and a documentation map that future sessions land on first.

## What Changes

- **CSharpier** becomes the single formatter: pinned as a local dotnet tool (`.config/dotnet-tools.json`), configured by `.csharpierrc.yaml`, checked by the verify gate on both platforms; `dotnet format` keeps analyzer and style duties with whitespace formatting and `IDE0055` disabled (they conflict with CSharpier). One one-time reformat commit, tracked in `.git-blame-ignore-revs`.
- **markdownlint-cli2** lints `README.md`, `AGENTS.md`, `docs/` and `adr/` with a committed config, pinned via `npx`, as part of the verify gate; `openspec/` stays out (tool-managed).
- **Contribution templates**: a PR template carrying the review-map format this project already uses, plus bug and feature issue templates.
- **CodeQL** code scanning for C#: on pushes to `main`, pull requests and a weekly schedule; results in the Security tab.
- **Documentation entry points**: a rewritten `README.md` (what it is, status, quick start, links), a new `docs/README.md` (map of every document kind plus the writing guideline), a short guide note, and an `AGENTS.md` pointer so every session finds the map.
- **ADR-0011** records the tooling decisions; Dependabot stays deferred (the pinned-package culture would fight its PRs).

## Capabilities

### New Capabilities
- `repo-quality`: deterministic formatting, documentation lint, contribution templates, and the documentation map and guideline.

### Modified Capabilities
- `ci-pipeline`: adds code scanning to the automation contract.
- `repo-foundation`: the verification gate now also checks formatting and documentation lint.

## Impact

- New: `.config/dotnet-tools.json`, `.csharpierrc.yaml`, `.csharpierignore`, `.markdownlint-cli2.yaml`, `.git-blame-ignore-revs`, `.github/pull_request_template.md`, `.github/ISSUE_TEMPLATE/`, `.github/workflows/codeql.yml`, `docs/README.md`, `adr/0011-repo-quality-tooling.md`.
- Modified: `.editorconfig` (IDE0055 off), `.gitignore`, `scripts/verify.sh`, `scripts/verify.ps1`, `scripts/gate-tests.sh`, `README.md`, `docs/guide.md`, `AGENTS.md` (protected file — edits quoted in design.md), and every `.cs` file once (the one-time reformat).
- No new NuGet packages: CSharpier is a pinned local dotnet tool, markdownlint-cli2 runs via pinned `npx`. Node.js becomes a development prerequisite for the docs lint step.
