# 0025 — Verified edits: compile before write, diagnose before apply

- Status: accepted — 2026-10-30 (maintainer sign-off with amendments folded in)
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

## Decision

- **C# only, first slice.** `Lunate.Roslyn` registers the C# verifier through the public
  write-verifier seam. TypeScript is tracked as #340: the LSP extension registers a verifier for
  `*.ts`/`*.tsx`.
- **The check is staged content compiled against its project, in memory.** The project compiles
  because that is what typechecking needs; source generators run in the in-memory compilation. A
  multi-targeted project checks only the TFM loaded by the workspace; dependents are out of scope.
  No `dotnet build` is run by verification.
- **Refuse only what the edit introduces.** Compare baseline and staged errors as multisets keyed
  by `(code, message)`, line-independently; refuse when a key's count increases. Pre-existing
  errors, warnings, analyzer findings and dependents' errors never refuse. After two refusals of a
  file in one run, the third attempt writes unverified; refusal text lists the diagnostics but does
  not advertise the bypass. `verify: block | warn | off` defaults to `block`; `warn` writes and
  reports unverified with diagnostics, while `off` skips verification.
- **A budget, a ceiling, and visible degradation.** Recalibrate the ceiling by ADR-0005 and pin it
  in the perf suite; the ceiling is at most 10 s. A timeout writes unverified with its reason. The
  TUI live area shows `verifying… <elapsed>`; Esc cancels the run as usual and no write occurs.
- **The host calls a public verifier seam.** `Lunate.Extensibility.Abstractions` lets extensions
  register verifiers for manifest-declared file patterns. The host calls after approval between
  read/normalize and apply/write, with a bounded duration. Results are verified, unverified with a
  reason, or refused with diagnostics in `StructuredContent`. Verifiers cannot write or modify
  content. The `ToolCalling` hook and extension-tool rejection reasons stand; the seam is the third
  option without their problems. The `BashGuard` precedent remains core-internal.

## Consequences

- The implementation adds a public verifier registration contract and a manifest declaration;
  `Lunate.Roslyn` supplies the C# verifier through the seam.
- An edit before any explicit `cs_*` call has loaded the workspace writes unverified with a
  `workspace-not-loaded` reason; verification does not trigger or prewarm that load under ADR-0021.
- A dependent API removal is caught after the fact via `cs_diagnostics`/`lsp-diagnostics`, not by
  the per-edit check — margin accepted and recorded here.
- ADR-0017 records the public extension seam addendum. The implementation change carries the
  red-green tests and perf-suite calibration.
