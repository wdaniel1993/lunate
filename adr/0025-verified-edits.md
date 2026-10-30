# 0025 — Verified edits: compile before write, diagnose before apply

- Status: proposed — 2026-10-30 (maintainer sign-off pending)
- Date: 2026-10-30
- Design: [`openspec/changes/verified-edits/design.md`](../openspec/changes/verified-edits/design.md) (#184)
- Relates to: ADR-0006 (in-process Roslyn), ADR-0012 (tool contract), ADR-0014 (C# backend
  interface), ADR-0021 (per-workspace services and laziness)

## Context

An edit that cannot compile is a failed turn the user pays for twice. The `write`/`edit` tools
land content without consulting the C# engine the session already carries; `cs_diagnostics` can
report the damage only after the write. #184 asks for verified edits — compile-before-write,
diagnose-before-apply — and names the open questions first. Measured on this repository
(node reuse disabled):

- `dotnet build` costs 10.7 s per no-op project build (13.7 s after a one-file change) and
  41–54 s per incremental solution build.
- The in-process Roslyn backend costs 2.6 s warm per changed-file check after a real edit
  (13–18 ms unchanged), 28.7 s cold first load, against 59 Roslyn projects / 1 649 documents in
  this workspace.

The post-write half of the idea already exists as the supported `lsp-diagnostics` extension, which
attaches a language server's diagnostics to a `write`/`edit` result through a `ToolResultReady`
hook, bounded and quiet on failure — the mechanism is proven; gating is not wired anywhere.

## Decision (proposed)

- **C# only, first slice.** Verification covers `.cs` staged content through the in-process
  Roslyn backend; TypeScript and every other language is out of the first slice. The second
  language shape is `lsp-diagnostics` generalized to a per-language server (post-write), to be
  decided in its own change.
- **The check is staged content compiled against its project, in memory.** The project compiles
  because that is what typechecking needs; the diagnostic filter is the staged file. No
  `dotnet build` is run by verification; dependents are not checked per edit.
- **Refuse only what the edit introduces.** Baseline (on-disk text) vs staged text errors,
  compared by code and message line-independently; only new compiler errors refuse. Pre-existing
  errors, warnings, analyzer findings and dependents' errors are reported, never a refusal. A
  two-refusals-per-file-per-run cap writes unverified on the third attempt — a refusal is a gate,
  never a lock-out.
- **A budget, a ceiling, and a loud degradation.** Budget calibrated by the ADR-0005 rule against
  the warm check; recommended ceiling 10 s, superseding by recalibration in the implementation
  change. Past the ceiling or without a usable backend, the write proceeds unverified and says so
  in one line; the write is never cancelled and never silent.
- **The guard lives in the tools.** The smallest thing that works with the landed extension
  contract: a core guard inside `WriteTool`/`EditTool` (the `BashGuard` precedent), acquiring the
  existing per-workspace backend, inside the mutation-queue step that already owns
  read-and-apply. Not a hook (the hook envelope is the 500 ms slow threshold with a fail-safe
  block policy — the opposite of unverified-continue) and not an extension (the contract names no
  veto surface for another tool's call).

## Consequences

- One additive `ICSharpBackend` member in the later implementation change; no public surface
  change beyond it, no hook contract change, no manifest change.
- The first verified edit in a cold workspace may degrade to unverified (cold load measured
  28.7 s here); its result names it and by design nothing is primed at startup (ADR-0021's
  laziness stands).
- A dependent API removal is caught after the fact via `cs_diagnostics`/`lsp-diagnostics`, not by
  the per-edit check — margin accepted and recorded here.
- If accepted, ADR-0025 stands beside ADR-0012 as the tool-contract addendum for the write path;
  the implementation change carries the red-green tests and the perf-suite calibration.
