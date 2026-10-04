## Why

Sigma's plan assumes a repository where the build is clean by construction — warnings are errors, layering is enforced by a test, public API changes are visible, and one command decides "done". None of that exists yet; this change (guide card T-01) creates the foundation so every later change lands on rails instead of convention.

## What Changes

- New solution `sigma.sln`: six source projects (`Sigma.Ai`, `Sigma.Agent`, `Sigma.Tui`, `Sigma.Protocols`, `Sigma.Roslyn`, `Sigma.Coding`) and one test project per source project — empty but building.
- `Directory.Build.props` (net10.0, nullable, warnings as errors, deterministic builds, code style enforced), `global.json` (SDK pin) and `.editorconfig`.
- Public API tracking (`Microsoft.CodeAnalysis.PublicApiAnalyzers` + `PublicAPI.Shipped/Unshipped.txt`) on the four library projects.
- Minimal `sigma --version` entry point in `Sigma.Coding`; no other behaviour.
- `scripts/verify.sh` and `scripts/verify.ps1` (build → tests → single-file publish → startup budget → format → public API diff) plus `scripts/perf.sh` (hyperfine-based startup budget).
- An architecture test that fails on upward project references.

## Capabilities

### New Capabilities
- `repo-foundation`: the repository's build-and-verification contract — enforced layering, warnings-as-errors, public API visibility, the `sigma --version` entry point, and the verify/perf gates later changes extend.

### Modified Capabilities
- None.

## Impact

- Repo root (`Directory.Build.props`, `global.json`, `.editorconfig`), `src/`, `tests/`, `scripts/`, `adr/`.
- No runtime behaviour beyond `sigma --version`; no public API beyond the entry point.
- NuGet references are limited to packages the guide's tech stack already approves: xUnit v3 (test projects) and `Microsoft.CodeAnalysis.PublicApiAnalyzers` (libraries).
