# Tasks: cli-trust-command

## 1. Shared pending-trust inventory

- [ ] 1.1 Extract the discovery + evaluation logic behind `InteractiveSession.CountPendingTrustQuestions` into a shared internal inventory (pending extensions with descriptor + hash + files-changed flag; pending project mcp.json with path + parsed servers + hash; repository records) that both the session and the command use; no behavior change in the session.
- [ ] 1.2 Unit tests: the inventory matches the session count for (a) untrusted extension, (b) changed-hash extension, (c) untrusted mcp.json, (d) new worktree of a trusted repository, (e) nothing pending.

## 2. TrustCommand

- [ ] 2.1 Implement `TrustCommand` (inspect / `--yes` / `--clear`) per design D3-D5: prints records + pending decisions with the interactive question's content; `--yes` prints then records the printed hashes; `--clear` clears both sections; discovery and hashing only (no loader load, no hooks, no server start).
- [ ] 2.2 Unit tests (in-process, seams store path): inspect records nothing; `--yes` records both sections with the printed hashes; second `--yes` idempotent; `--clear` clears; changed-files wording on re-prompt; new-worktree entry recorded by `--yes`.
- [ ] 2.3 Never-loads proof: a test extension whose factory writes a marker — `lunate trust --yes` records and the marker never appears.

## 3. CLI glue + help

- [ ] 3.1 Dispatch `args[0] == "trust"` in `Cli.Run` (the `--discover` pattern): flags parsed (`--yes`, `--clear`), conflicts and unknown flags → usage on stderr + exit 2, unexpected failure → exit 1, success → exit 0; stdout carries the report.
- [ ] 3.2 Help text: a `lunate trust [--yes|--clear]` usage block saying what it does and that `--yes` records exactly what it prints.
- [ ] 3.3 Tests: dispatch (usage errors, exit codes) through `Cli.Run`.

## 4. Refusal hints name both approval paths

- [ ] 4.1 `NonInteractiveTrustPrompt` and the `McpConfigTrust` caller log now point at an interactive session **or** `lunate trust`.
- [ ] 4.2 Update the tests asserting the exact hint sentence; keep the scenarios' deny-by-default behavior unchanged.

## 5. End-to-end through the CLI

- [ ] 5.1 Print-mode e2e: untrusted project extension → refusal hint names `lunate trust`; after `lunate trust --yes` (same store) the print session loads the extension (factory marker present) and logs no hint.
- [ ] 5.2 Print-mode e2e for the project `mcp.json`: refusal hint names both paths; after `lunate trust --yes` the project server starts; `lunate trust` alone starts nothing.
- [ ] 5.3 New process-spawning tests join the `mcp-processes` collection.

## 6. Gate and docs

- [ ] 6.1 `scripts/verify.sh` green (both cultures), `openspec validate --all --strict` clean, csharpier + markdownlint clean.
- [ ] 6.2 Spec deltas stay in sync at archive (extensions: trust requirement + command requirement; protocols: mcp.json hint + read sentence + start-nothing scenario).
