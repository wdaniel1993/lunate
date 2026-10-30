# Tasks — verified edits design

## 1. Design artifacts

- [x] 1.1 Settle the five questions from #184 in `design.md`, each with a recommendation and its
      measured cost: language scope, staged content vs whole project, the unfixable-diagnostic
      rule, the invisibility budget, and the placement against the landed extension contract.
      C# first via the in-process Roslyn backend; staged text compiled against its project,
      changed-file scope; refuse only introduced compiler errors with a two-refusal cap; warm
      p50 target and a 10 s ceiling with loud unverified degradation; guard inside the
      `write`/`edit` tools.
- [x] 1.2 Measure the numbers on this repository and record how each was taken (`design.md` §1.2):
      MSBuild project/solution builds (node reuse disabled) and Roslyn cold/warm checks driven
      through `lunate mcp roslyn`.
- [x] 1.3 Add the delta spec: a new `verified-edits` capability covering staged-content
      verification, the refusal rule, budget/degradation and the placement.
- [x] 1.4 Record the decision as **proposed** ADR-0025 (`adr/0025-verified-edits.md`) and the
      change-local ADR manifest (`adr.md`).
- [x] 1.5 Record deliberate non-goals: no TypeScript/LSP in the first slice, no analyzer gating,
      no whole-solution per-edit check, no hook or extension-surface change, no startup priming.

## 2. Validation

- [x] 2.1 `openspec validate verified-edits --strict` passes.
- [x] 2.2 `openspec validate --all --strict` passes.
- [x] 2.3 `python3 scripts/docs-sync --check` passes.
- [x] 2.4 `npx --yes markdownlint-cli2@0.23.3` passes for `adr/0025-verified-edits.md`.
- [x] 2.5 Draft PR open with `Refs #184` in the first line of the body; body states one-line
      recommendations for each of the five questions; issue #184 carries the PR link.

## 3. Sign-off (blocking)

- [ ] 3.1 Maintainer sign-off on the five recommendations and proposed ADR-0025 — none yet; the
      PR proposes and stops.

## 4. Follow-up changes (not part of this change)

- [ ] 4.1 `verified-edits-implementation`: the additive `ICSharpBackend` staged-diagnostics
      member plus the in-tool guard in `WriteTool`/`EditTool`, test-first (refusal,
      pre-existing pass-through, cold-overrun degradation, retry cap), budget ceiling
      recalibrated by the ADR-0005 rule and pinned in the perf suite; docs (`docs/guide.md`,
      `docs/architecture.md`) claim updated only when the behaviour is true.
- [ ] 4.2 The second-language decision (likely `lsp-diagnostics` generalized to a per-language
      server for TypeScript post-write diagnostics), explicitly out of the first slice.
