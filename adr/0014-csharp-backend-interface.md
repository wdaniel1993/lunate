# 0014 — C# tools behind a backend interface

- Status: proposed — 2026-10-06
- Date: 2026-10-06
- Relates to: ADR 0006 (Roslyn placement), ADR 0012 (tool contract)

## Context

C# language servers (Microsoft's Roslyn language server, csharp-ls) run Roslyn and MSBuildWorkspace too, so the real choice is not "Roslyn vs LSP" but "in-process API vs LSP protocol on the same engine". The bet — tested by T-35 — is tool design: name-based tools for agents instead of editor-style file/line/column requests, diagnostics straight from the compilation, refactors returned as diffs, zero setup, and control over load failures. Keeping the tools decoupled from the concrete engine keeps that bet reversible.

## Decision

- The C# tools (`cs_diagnostics`, `cs_find_symbol`, `cs_find_references`, `cs_outline`, `cs_rename`) are `ITool`s whose contracts do not depend on the backend.
- They call an `ICSharpBackend` with name-based operations, for example:
  - `LoadAsync(solutionPath)` → load status, including partial-load diagnostics;
  - `GetDiagnosticsAsync(scope: files | solution)`;
  - `FindSymbolAsync(name)` / `FindReferencesAsync(symbol)`;
  - `GetOutlineAsync(file)`;
  - `PlanRenameAsync(symbol, newName)` → proposed edits as diffs — never applied by the backend; the edit/approval path applies them.
  Exact signatures are decided in T-25; this ADR fixes the shape.
- Default backend: in-process Roslyn (ADR 0006). An LSP backend (Microsoft's Roslyn language server or csharp-ls) is a possible later implementation, not planned work.
- Position lookup (name → file/line/column) lives in the backend, never in the model's tool calls.

## Consequences

- The tool surface stays stable if the engine changes; ADR 0006's operational rules (restore check, SDK mismatch, partial loads) are the reliability edge and belong to the backend implementation.
- T-25 implements `ICSharpBackend` and the in-process backend together with `cs_diagnostics`.
- Rename planning returns diffs, so refactors flow through the existing approval policy and edit path — the backend never mutates the workspace.
