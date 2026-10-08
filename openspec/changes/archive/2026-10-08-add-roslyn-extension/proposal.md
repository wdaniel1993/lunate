# add-roslyn-extension

## Why

Card T-25 (deps T-36): the .NET edge — `ICSharpBackend` + the in-process Roslyn backend + the `cs_diagnostics` tool, per accepted ADR-0014 (backend interface, exact signatures here) and ADR-0006 (in-process Roslyn; build host beside single-file; restore precondition; partial loads surfaced; SDK mismatch soft-fail). S-4 measured the bar: fixture cold load ~0.65 s, Polly ~3.3 s, warm edit-to-diagnostics ~2 ms / ~100 ms. Done-gate: detects an error introduced by `edit`; first-call budget met.

Packages (approved by the maintainer 2026-10-08; listed in the guide's tech table): `Microsoft.CodeAnalysis.CSharp.Workspaces` 5.9.0, `Microsoft.CodeAnalysis.Workspaces.MSBuild` 5.9.0, `Microsoft.Build.Locator` 1.11.2 (spike-verified versions), plus `Microsoft.Build.Framework` with `ExcludeAssets=runtime` per ADR-0006.

## What Changes

- **`Lunate.Roslyn` gains**: `ICSharpBackend` (name-based ops; T-25 pins `LoadAsync` + `GetDiagnosticsAsync` + a file-change hint; the remaining ops land with their tools in T-26/T-31), the in-process `RoslynBackend` (MSBuildLocator init on first use, MSBuildWorkspace per solution, restore check before compiling, `WorkspaceFailed` collected and surfaced, SDK mismatch explained, never a raw exception), a workspace manager (solution discovery, **cap 2 LRU** per the extensibility spec, deferred assembly loading — no Roslyn/MSBuild type touched before the first call), and file freshness (changed files applied to the workspace without reloading; `NotifyFileChanged` entry point for the future FileChanged bus).
- **`cs_diagnostics` tool**: scope `changed` (default) | `solution`; readable summary + UI-only details (errors/warnings with file, 1-based line/column, id, message; bounded); `read-only` annotation. Loads the backend lazily on first call.
- **Fixtures**: `tests/fixtures/solutions/` — a package-free console app (primary gate) and a library-with-test project (per the guide; used where packages don't compromise offline runs).
- **Spec delta**: `agent-tools` gains the `cs_diagnostics` requirement + scenarios (error after edit; no SDK; restore required; partial load).
- No hooks/services wiring this change (the built-in extension is an in-tree lazy module; loader/child-process mechanics per ADR-0006 note).

## Impact

- `src/Lunate.Roslyn` (first real implementation; PublicAPI tracked — entries added), `tests/Lunate.Roslyn.Tests`, fixtures, layering gate edges (`Lunate.Roslyn -> Lunate.Agent`).
- No new ADR (implements accepted ADR-0006 + ADR-0014 + S-4 constraints).
