# Tasks — bash change marks (design-first)

- [x] 0.1 Recon: bus publish path (`ToolContext.FileChanges` / `IFileChangeSink`), `BashTool` shape, `WorkspaceServices` mark/reload routing, the D9 residual limit (archived `roslyn-file-bus-sync`).
- [x] 0.2 Design drafted (this change) with the five decisions; sign-off requested (card).
- [x] 1.1 (after sign-off) `BashChangeTracker` (diffed publisher: per-run snapshot of HEAD + content hashes; rules a/b/c) + `BashTool` wiring (scan after success/non-zero/timeout and on cancellation with an independent timeout; `source = bash`; `FileChangeEvent.Source` + `FileChangedPayload.Source` + PublicAPI).
- [x] 1.2 Tests: tracker suite 13/13 (real repos: no-re-flag, same-stamp caught, revert published, commit quiet, branch switch, untracked, moves, renames, subdir) + `BashToolChangeTests` 3/3 + `RoslynFileBusSyncTests` 9/9 (same-stamp bash edit without reload; untracked-dir new file via the reload).
- [x] 1.3 Cost measured + recorded in `design.md` (~0.01 s project repo; 0.19–0.52 s larger repo).
- [x] 1.4 Spec deltas (`agent-tools`, `extensions`); validate OK.
- [ ] 2.1 `scripts/verify.sh` green (both cultures); adversarial review; archive; PR.
