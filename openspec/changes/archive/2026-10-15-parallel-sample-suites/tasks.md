# Tasks

## 1. Draft

- [ ] 1.1 Draft proposal/design/tasks; `openspec validate --all --strict` green.
- [ ] 1.2 Branch `change/parallel-sample-suites`; commit + push the draft.

## 2. Flip + local evidence

- [x] 2.1 Remove `[assembly: Parallelization(Mode = ParallelMode.None)]` from all seven sample suites' `AssemblyInfo.cs` (subagents, model-router, lsp-diagnostics, code-mode, mcp-from-extension, ui-showcase, worktree-tasks).
- [x] 2.2 Build Release; run each suite binary 5× in a row (parallel collections on); record pass/fail per run.
- [x] 2.3 If a suite fails locally 5× evidence: apply the D2 ladder for it (bounded → restore+document) before proceeding.

## 3. Gate

- [x] 3.1 `scripts/verify.sh` green (both cultures); csharpier/style clean.

## 4. Review

- [x] 4.1 Adversarial review; fix findings.

## 5. Archive + PR

- [x] 5.1 Archive; tick tasks; push.
- [x] 5.2 Open the PR (body: review map + the evidence protocol).

## 6. CI evidence loop (acceptance)

- [ ] 6.1 Watch CI; on a load-dependent failure apply the D2 ladder for the failing suite and repeat. Acceptance = **three consecutive green runs** (all legs) on the final tree — the archive commit's run counts.
- [ ] 6.2 Update the `lunate-dev` skill's serialization rule to the outcome (parallel where proven; bounded where bounded; serialize only where documented).
- [ ] 6.3 Closing comment on the PR with the run ids of the three green runs; the maintainer merges.
