# Tasks — verified edits design

## 1. Design artifacts

- [x] 1.1 Settle the five questions from #184 in `design.md`, each with a recommendation and its
      measured cost: language scope, staged content vs whole project, the unfixable-diagnostic
      rule, the invisibility budget, and the placement against the landed extension contract.
      C# first via the in-process Roslyn backend; staged text compiled against its project,
      changed-file scope; refuse only introduced compiler errors with a two-refusal cap; warm
       p50 target and a 10 s ceiling with loud unverified degradation; public, manifest-declared
       write-verifier extension point invoked by the host after approval.
- [x] 1.2 Measure the numbers on this repository and record how each was taken (`design.md` §1.2):
      MSBuild project/solution builds (node reuse disabled) and Roslyn cold/warm checks driven
      through `lunate mcp roslyn`.
- [x] 1.3 Add the delta spec: a new `verified-edits` capability covering staged-content
       verification, the multiset refusal rule, setting, budget/degradation and verifier seam.
- [x] 1.4 Record the decision as ADR-0025 (`adr/0025-verified-edits.md`) and the
      change-local ADR manifest (`adr.md`).
- [x] 1.5 Record deliberate non-goals: no TypeScript verifier in the first slice, no analyzer
       gating, no whole-solution per-edit check, no `ToolCalling` hook or extension tool, no
       startup priming.

## 2. Validation

- [x] 2.1 `openspec validate verified-edits --strict` passes.
- [x] 2.2 `openspec validate --all --strict` passes.
- [x] 2.3 `python3 scripts/docs-sync --check` passes.
- [x] 2.4 `npx --yes markdownlint-cli2@0.23.3` passes for `adr/0025-verified-edits.md`.
- [x] 2.5 Draft PR open with `Refs #184` in the first line of the body; body states one-line
      recommendations for each of the five questions; issue #184 carries the PR link.

## 3. Sign-off (blocking)

- [x] 3.1 Maintainer sign-off 2026-10-30 on the five recommendations and ADR-0025, with amendments
       folded in — a public verifier seam instead of a core guard (ADR-0017 addendum), a multiset
       baseline, source generators and a named TFM, the `verify` setting, the live-area line and Esc.

## 4. Follow-up changes (not part of this change)

- [x] 4.1 `verified-edits-implementation`: public manifest-declared verifier registration, host
       invocation after approval, C# verifier registered by `Lunate.Roslyn`, the `verify` setting,
       timeout/cancellation, refusal escape and TUI progress; test-first per its implementation
       tasks, recalibrate the ceiling by ADR-0005 and pin it in the perf suite; update docs only when
       the behavior is true. Merged via #341 (`18fc3580`).
- [x] 4.2 #340: the LSP extension registers a verifier for `*.ts`/`*.tsx`; completed by #362
       (`6d0d078d`).
