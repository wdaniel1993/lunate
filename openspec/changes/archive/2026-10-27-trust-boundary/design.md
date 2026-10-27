# Design: the approval panel is the boundary

## Pinned decisions

- **Risk derivation** is `ReadOnly` only when `ReadOnly && !Destructive && !OpenWorld`; `Execute` when there are no annotations or when `Destructive`/`OpenWorld` is set; `Write` otherwise. An explicit `ToolRisk` still wins.
- **The ACP approver** bypasses only `ReadOnly`. Writes and commands ask, whatever the policy.
- **High-risk classification** lives in one place (`HighRiskCall`) and covers: bash running `lunate trust|install|uninstall` (absolute path, `env -u LUNATE_SESSION`, `bash -lc`, flags before the subcommand), and any bash/write/edit path resolving inside `LunateHome.Store`. Resolution, not string matching: `~`, `$HOME` and `$LUNATE_HOME` are expanded, then the path is compared against the configured state root.
- **A high-risk call** always prompts under `ask` and `auto-edit`; the session's allow-always does not cover it; print mode declines it.
- **`--yolo`** keeps the documented meaning: the panel is off. The only addition is one startup line saying so. Contradiction → stop and report.
- **A trust handler is a veto.** `Deny` refuses; `Allow` does not stand in for the prompt.
- **`lunate trust --yes`** requires `!Console.IsInputRedirected`, seamed through `Cli.Run` so tests can drive it. `--yes --digest` is exempt. The session-marker refusal runs first for both.

## Alternatives considered

- *Make `--yolo` refuse high-risk calls.* Rejected: `--yolo` means "no panel", and silently keeping a panel for selected calls would make the flag lie. It announces itself instead.
- *Deny the state root in the bash tool rather than classify it.* Rejected for 0.1.0: a deny-list inside the same process is bypassable by any other route to the filesystem. The OS guard is scheduled for 0.2 (`t_0cc0fd09`); until then the classification is defence in depth, not a boundary.
- *Make the trust handler's `Allow` skip the prompt.* Rejected: a global extension would then be able to answer a question that belongs to the user.

## Deviations

Filled during apply; see the PR body for the final list.
