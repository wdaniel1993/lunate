# 0011 — Repository quality tooling

- Status: accepted — 2026-10-06 (maintainer sign-off; change merged)
- Date: 2026-10-05

## Context

Formatting was enforced by `dotnet format` alone: it checks whitespace and style against `.editorconfig`, which yields acceptable but non-deterministic results — two editors (or a human and a model) can produce different but equally valid layouts. The repository is developed by humans and AI sessions in parallel and needs one canonical layout. Additionally, documentation has no map, no lint and no guideline, contribution templates do not exist, and code scanning is not in place before the codebase grows.

## Decision

1. **CSharpier** is the single C# formatter, pinned as a local dotnet tool with a committed configuration. Whitespace formatting and `IDE0055` leave `dotnet format`'s scope; style and analyzer checks remain.
2. **markdownlint-cli2** (pinned via `npx`, committed configuration) lints the documentation as part of the verify gate.
3. **CodeQL** (C#, `security-extended`) scans pushes, pull requests and a weekly schedule; results surface in code scanning.
4. **PR and issue templates** institutionalize the review-map flow and standard contribution entry points.
5. **`docs/README.md`** is the documentation map and writing guideline; `README.md` is the project entry point; `AGENTS.md` points to the map.
6. **Dependabot stays deferred**: the repository pins packages deliberately, and automated bump PRs would fight that policy.

## Consequences

- One-time reformat commit (tracked in `.git-blame-ignore-revs`); deterministic style afterwards for every contributor, human or model.
- Node.js becomes a development prerequisite (docs lint only).
- The verify gate is stricter; failures are actionable and reproducible locally.
- Revisit Dependabot when the package-pinning policy relaxes.
