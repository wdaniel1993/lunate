# Tasks: Approval prompt, status footer, key bindings (T-21)

## 1. Key routing + Ctrl+C window

- [x] 1.1 `KeyRouter` + `RoutedKey`; the guide-table matrix as one scripted theory (every binding: Enter, Alt+Enter, Ctrl+J, Esc, Ctrl+C, Ctrl+L, Up, Down) + edge falsifiers (Ctrl+J before generic ctrl, Shift+Enter falls through, unknown keys → Edit)
- [x] 1.2 `CtrlCQuitWindow` on `IScheduler`; virtual-time tests: clear on non-empty, arm on empty, quit within 2 s, re-arm after expiry, disarm on clear; never cancels a turn (no cancel path exists)

## 2. Approval prompt

- [x] 2.1 `ApprovalPromptModel` + `Decide`: y/n/a bare only; Enter → Deny; Esc/other → null; modifier falsifiers (Ctrl+Y does not approve)
- [x] 2.2 `ApprovalPromptRenderer` + golden `approval-prompt.txt`; escaping (bracket payloads in tool name and summary); ANSI pin for the question marker

## 3. Status footer

- [x] 3.1 `GitBranchReader`: normal repo, worktree `.git` file, detached HEAD, missing repo, unreadable HEAD — temp-dir tests
- [x] 3.2 `StatusFooterModel` + renderer: invariant token formatting (raw, k, M; boundary 999/1000/999_999/1_000_000), percent edge (zero window omits), degradation ladder (branch dropped, then cwd), goldens `footer-full.txt`, `footer-narrow.txt`, `footer-minimal.txt`

## 4. Close

- [ ] 4.1 csharpier; `bash scripts/verify.sh` green (incl. de-AT); `openspec validate add-prompt-footer-bindings --type change --strict`; self-review; commit per group; no push
- [ ] 4.2 Deviations recorded in design.md; note the T-22 seams (`ApprovalRequested` → model, `Always` memory, footer notice wiring)
