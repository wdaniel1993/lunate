# Tasks: cli-trust-command

## 1. Shared pending-trust inventory

- [x] 1.1 Extract the discovery + evaluation logic behind `InteractiveSession.CountPendingTrustQuestions` into a shared internal inventory (pending extensions with descriptor + hash + files-changed flag; pending project mcp.json with path + parsed raw servers + hash; repository records) that both the session and the command use; no behavior change in the session.
- [x] 1.2 Unit tests: the inventory matches the session count for (a) untrusted extension, (b) changed-hash extension, (c) untrusted mcp.json, (d) new worktree of a trusted repository, (e) nothing pending.

## 2. Raw-definition hashing for the project mcp.json

- [x] 2.1 Replace the file-content hash with a hash over the servers' raw parsed definitions (name, command, args, env, cwd, call timeout as written; unknown fields excluded), canonically serialized, name-sorted, invariant — the trust evaluation and recording use it everywhere (session prompt path and command).
- [x] 2.2 Tests: identical definitions with different formatting/key order hash equal; environment values never influence the hash; a secret-shaped value never appears in the trust file; a definition change re-prompts; extension directory hashing unchanged.

## 3. TrustCommand

- [x] 3.1 Implement `TrustCommand` per design D3-D12: inspect prints records + pending decisions (interactive question content, shared command/truncate helper) + the pending digest; `--yes` prints then records the printed hashes; `--yes --digest <sha>` records only on match (mismatch: nothing recorded, pinned + computed digest + pending set shown, exit 4); `--json` prints records/pending/digest as one canonical document; `--clear` prints what it removed; discovery and hashing only (no loader load, no hooks, no server start).
- [x] 3.2 Exit codes: 0 success / nothing pending, 1 unexpected failure, 2 usage, 3 inspect found pending decisions, 4 digest mismatch.
- [x] 3.3 Unit tests (in-process, seams store path): inspect records nothing and exits 3 with pending / 0 without; `--yes` records both sections with the printed hashes; second `--yes` idempotent; digest match records, mismatch records nothing + exit 4 + difference shown; digest stable across invocations and formatting-only edits, changes on content change; `--json` shape + canonical newlines + null digest when nothing pending; `--clear` clears and prints the removed lines; `--yes --clear`, `--json --yes`, unknown flags → exit 2; changed-files wording on re-prompt; new-worktree entry recorded by `--yes`.
- [x] 3.4 Never-loads proof: a test extension whose factory writes a marker — `lunate trust --yes` records and the marker never appears.

## 4. CLI glue + help

- [x] 4.1 Dispatch `args[0] == "trust"` in `Cli.Run` (the `--discover` pattern): flags parsed (`--yes`, `--digest <sha>`, `--json`, `--clear`), conflicts and unknown flags → usage on stderr + exit 2, unexpected failure → exit 1; stdout carries the report.
- [x] 4.2 Help text: a `lunate trust [--yes [--digest <sha>]] [--json] [--clear]` usage block: what it does, the exit codes, and that `--yes` records exactly what it prints (for checkouts you control; use `--digest` for CI).
- [x] 4.3 Tests: dispatch (usage errors, exit codes) through `Cli.Run`.

## 5. Refusal hints name both approval paths

- [x] 5.1 `NonInteractiveTrustPrompt` and the `McpConfigTrust` caller log now point at an interactive session **or** `lunate trust`.
- [x] 5.2 Update the tests asserting the exact hint sentence; keep the scenarios' deny-by-default behavior unchanged.

## 6. End-to-end through the CLI

- [x] 6.1 Print-mode e2e: untrusted project extension → refusal hint names `lunate trust`; after `lunate trust --yes` (same store) the print session loads the extension (factory marker present) and logs no hint.
- [x] 6.2 Print-mode e2e for the project `mcp.json`: refusal hint names both paths; after `lunate trust --yes` the project server starts; `lunate trust` alone starts nothing.
- [x] 6.3 Pinning e2e: `lunate trust --json` digest → `--yes --digest` records; a stale digest records nothing.
- [x] 6.4 New process-spawning tests join the `mcp-processes` collection.

## 7. Docs and gate

- [x] 7.1 README: a short "trust in scripted runs" subsection (per user/machine records — ephemeral CI runners run the trust step each job; `--yes` warning; the pinning flow).
- [x] 7.2 `scripts/verify.sh` green (both cultures), `openspec validate --all --strict` clean, csharpier + markdownlint clean.
- [x] 7.3 Spec deltas stay in sync at archive (extensions: trust requirement + command requirement + security posture; protocols: mcp.json hint + read sentence + raw-definition hash + start-nothing scenario).
