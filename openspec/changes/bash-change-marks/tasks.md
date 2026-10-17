# Tasks — bash change marks (design-first)

- [x] 0.1 Recon: bus publish path (`ToolContext.FileChanges` / `IFileChangeSink`), `BashTool` shape, `WorkspaceServices` mark/reload routing, the D9 residual limit (archived `roslyn-file-bus-sync`).
- [x] 0.2 Design drafted (this change) with the five decisions; sign-off requested (card).
- [ ] 1.1 (after sign-off) `GitChangeScanner` + `BashTool` wiring.
- [ ] 1.2 Tests: scanner unit + real-repo fixtures + `BashToolTests` sink assertions + the real-Roslyn same-stamp integration test.
- [ ] 1.3 Measure the per-call scan cost on a large repo; record in `design.md`.
- [ ] 1.4 Spec deltas (`agent-tools`, `extensions`); validate.
- [ ] 2.1 `scripts/verify.sh` green (both cultures); adversarial review; archive; PR.
