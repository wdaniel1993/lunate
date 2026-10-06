## Context

The maintainer asked for standard code-quality tooling (CSharpier first), a documentation guideline, and a README that future human and AI sessions can start from. Current state: `dotnet format` checks formatting against `.editorconfig`; there is no formatter with deterministic output, no docs lint, no contribution templates, no code scanning, and `docs/` has no map — the guide, specs, ADRs and spikes exist but nothing points at them.

## Goals / Non-Goals

**Goals:** deterministic formatting that yields identical output for humans and models; a stricter verify gate (formatting, docs lint) with everything verifiable locally; a documentation map and guideline that future sessions land on; the standard contribution surface (templates); code scanning in place before the codebase grows.

**Non-Goals:** Dependabot (deferred — see ADR-0011), `CONTRIBUTING.md`, a docs website, any CI topology change beyond adding the CodeQL workflow.

## Decisions

- **CSharpier 1.3.0, pinned as a local dotnet tool.** Deterministic and opinionated — exactly the property that keeps human and model output identical. `.config/dotnet-tools.json` via `dotnet new tool-manifest` + `dotnet tool install csharpier --version 1.3.0`; config `.csharpierrc.yaml` (printWidth 100, indentSize 4, useTabs false, endOfLine lf — matches `.editorconfig`); ignore `.csharpierignore` (`bin/`, `obj/`, `artifacts/`). One-time `dotnet csharpier format .` lands as a single commit (`style: apply CSharpier formatting (one-time)`) so the reformat is separable from behavior; `.git-blame-ignore-revs` lists that commit (GitHub honors it; locally `git config blame.ignoreRevsFile .git-blame-ignore-revs`, documented in `docs/README.md`).
- **`dotnet format` keeps style and analyzers only.** The verify gate runs `dotnet csharpier check .` for formatting plus `dotnet format style lunate.sln --verify-no-changes --no-restore` and `dotnet format analyzers lunate.sln --verify-no-changes --no-restore`; whitespace formatting is CSharpier's. `dotnet_diagnostic.IDE0055.severity = none` in `.editorconfig` (CSharpier's documented guidance — the formatting analyzer conflicts otherwise).
- **markdownlint-cli2 0.23.3 via pinned `npx`.** No `package.json` in a .NET repo; `npx --yes markdownlint-cli2@0.23.3` resolves the pinned version wherever Node exists, cached after the first run. Config `.markdownlint-cli2.yaml`: globs `README.md`, `AGENTS.md`, `docs/**/*.md`, `adr/**/*.md`; `MD013` off (long links are normal in docs); further disables only with a comment. `openspec/` is excluded (tool-managed). The verify step fails with an actionable message when Node is missing.
- **CodeQL**: `.github/workflows/codeql.yml` — csharp, `build-mode: manual` (`dotnet restore` + `dotnet build`; autobuild is opaque), `queries: security-extended`, `ubuntu-latest`, permissions `security-events: write`, triggers push/PR to `main` plus a weekly cron, fork guard. Free for public repositories; alerts are triaged (follow-ups, not blockers, unless security-relevant).
- **Templates**: `.github/pull_request_template.md` mirrors the review-map flow already in use (What this is / What lands / Review map / Verification / Notes). Issue templates `bug_report.yml` and `feature_request.yml` (GitHub YAML forms).
- **Documentation map and guideline** (`docs/README.md`): homes — `docs/guide.md` = the plan and status; `openspec/specs/` = behavior source of truth; `openspec/changes/` = in-flight proposals; `adr/` = decisions with context; `docs/spikes/` = evidence; `README.md` = entry point. Guideline: English-first; linted by markdownlint; keep current; one home per fact; link, don't copy; update specs, not prose, when behavior changes. `AGENTS.md` points to it so every session finds it.
- **AGENTS.md edits (protected file; exact text for approval)**:
  - After the intro lines: `- Documentation map and writing guideline: docs/README.md. Start there when adding, moving or looking for documentation.`
  - Build section, verify line becomes: `- scripts/verify.sh (Windows: scripts/verify.ps1) must pass before a change is done: build, tests, single-file publish, performance budgets, public API, formatting (CSharpier) and documentation lint checks.`
  - New Build bullet: `- C# code is formatted with CSharpier (dotnet csharpier format .); never hand-format. The gate checks it (dotnet csharpier check .); analyzer and style rules run via dotnet format style / dotnet format analyzers (whitespace formatting and IDE0055 are off).`
- **Gate self-test**: `scripts/gate-tests.sh` gains a formatting self-test — an unformatted temp file must fail `dotnet csharpier check`, the formatted one must pass — so a broken format gate is detected the same way a broken budget check is.

## Risks / Trade-offs

- [One-time reformat noise] → single dedicated commit + `.git-blame-ignore-revs`; formatting only, verify green before and after.
- [Formatter version drift] → pinned in the tool manifest; bumps are deliberate commits.
- [npx network dependency in the gate] → version pinned, cached after first run; actionable error when Node is absent; documented as a prerequisite.
- [The dotnet format split may surface findings the whitespace pass masked] → fix minimally in this change and note them in the commit message.
- [CodeQL noise on a small codebase] → security-extended queries; alerts triaged; can be narrowed later.
- [AGENTS.md is protected] → edits are quoted above and land only with maintainer approval; the apply stops on a permission prompt.

## Migration Plan

The reformat lands as its own commit; CI is unchanged except the new CodeQL workflow; no runtime behavior changes.

## Open Questions

- None blocking. Dependabot remains deferred; revisit when the package-pinning policy relaxes.
