# Proposal: print mode, non-interactive approval and the eval baseline (T-17)

## Why

The loop, tools, sessions, prompt and config all exist — but nothing outside tests can *run* the agent. Card T-17 closes that: print mode is the third consumer of the event stream (the guide's architecture principle: the core knows nothing about UIs; the TUI, print mode and ACP mode consume events), and it is the first frontend, so it forces the two questions every frontend needs answered: how does a run end visibly (stdout contract, exit codes) and what happens to approval when nobody can be asked.

It is also the scoreboard's first entry: `eval/tasks/` and `eval/results.csv` exist in the repo layout, and the guide promises a check-scripted task suite driven by `lunate -p --json`. T-35's head-to-head depends on this machinery, so T-17 builds the runner, the first tasks and the baseline row.

## What changes

- **Print mode** (`lunate -p "<prompt>"`, new `print-mode` capability): runs one prompt to completion; the final answer goes to stdout and exits — nothing else on stdout, diagnostics on stderr; `--json` streams every event as one JSON line (script and CI contract). Exit codes: 0 stop, 1 error, 2 length/step limit, 130 cancelled.
- **Non-interactive approval**: print mode cannot prompt, and silently running everything would betray the default policy. The run carries an approver that maps the resolved policy onto tool risk: `ask` (default) allows only read-only tools; `auto-edit` also allows file writes/edits; commands require the per-run `--yolo` flag (never a saved setting — guide). Denied calls surface as error results and a stderr line.
- **Settings reconciliation (agent-config delta)**: T-16 shipped `approval: ask | auto` as a placeholder; the guide's vocabulary is `ask | auto-edit`, with `yolo` only ever as a flag. Nothing consumed the old value, so it is corrected now: `auto-edit` replaces `auto`, and `yolo` in settings.json is rejected.
- **Eval baseline**: `eval/tasks/` gains its first task set (small fixture repos with a prompt and a check script), `scripts/eval.sh` runs them through `lunate -p --json --yolo`, records pass/fail, steps, tokens, time and edit tiers, and appends a row to `eval/results.csv` with the exact model id pinned. The first baseline row for the current toolset is produced and committed as part of this card.

## Done when

Print mode works end to end (replay-client tests for stdout/stderr/exit codes/JSONL; a real local run documented). The eval runner runs the suite offline against a stub, then live against the configured model, and `eval/results.csv` carries the baseline row (gate). No new packages.
