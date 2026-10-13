# Proposal: cli-trust-command

## Why

A CI job (or any scripted run) in a repository with project extensions or a project `.lunate/mcp.json` cannot load them: print and ACP sessions refuse an untrusted project extension/config with a hint pointing at the interactive path, and the trust must have been recorded earlier by an interactive session on that machine — which CI runners never do. Daniel's directive during the tui-native-trust-prompt sign-off (2026-10-11): check whether a deliberate pre-trust path exists; if not, add one rather than reviving a console prompt. The check found none (card `t_43f9e718`).

The refusal hints therefore dead-end for exactly the audience that needs them most — scripted runs — while the trust discipline itself (one-time per repository, content-hashed, re-prompted on change, never implicit) is sound and stays.

## What

- **A `lunate trust` command** (the first subcommand; the CLI is otherwise flags):
  - `lunate trust` — inspect: prints this repository's records, every pending decision with the same content the interactive trust question shows (extension id/version/directory; mcp.json path with server names and commands), and one digest over the pending decisions; records nothing. Exit 0 when nothing is pending, 3 when decisions are pending (a CI check).
  - `lunate trust --yes` — prints the same pending decisions, then records them (project extensions and the project `mcp.json`). Plain `--yes` is for checkouts the operator controls (never untrusted PRs/forks).
  - `lunate trust --yes --digest <sha>` — the CI form: records only when the recomputed digest matches the pinned one; a mismatch records nothing, shows the difference (pinned digest, computed digest, current pending set) and exits 4.
  - `lunate trust --json` — the inspect output contract: one JSON document with the records, pending decisions and digest.
  - `lunate trust --clear` — clears this repository's records and prints what it removed (parity with `/trust clear`).
  - Unknown or conflicting flags: usage on stderr, exit 2. Nothing pending: exit 0 (idempotent).
- **Discovery and hashing only**: the command never loads extension assemblies, never dispatches `ProjectTrust` hooks, and never starts an MCP server. It reuses the discovery + content-hash machinery the interactive session's pending-question count already uses (extracted into a shared inventory so the two cannot drift).
- **The mcp.json trust hash moves to the servers' raw definitions** — as written, before any `${VAR}` expansion, canonically serialized: machine-independent, no secret can influence it, and formatting-only edits stop re-prompting (existing records re-prompt once).
- **The hashes printed are the hashes recorded** (one evaluation per invocation), and the changed-files re-prompt wording matches the interactive question.
- **The refusal hints name both approval paths**: the extension and `mcp.json` non-interactive refusals now point at an interactive session *or* `lunate trust`.
- **Docs**: the README gains a short "trust in scripted runs" subsection (records are per user/machine — ephemeral CI runners run the trust step each job; the `--yes` warning; the pinning flow); the help text carries the one-line version.
- **Spec deltas**: extensions (the trust requirement's hint sentence + the command requirement + a security-posture requirement); protocols (the `mcp.json` requirement's hint, read sentence and raw-definition hash, plus a start-nothing scenario).

## Impact

- `src/Lunate.Coding/`: new `TrustCommand` (+ a small shared pending-trust inventory used by `InteractiveSession.Trust`, and a shared command/truncate helper used by both the command and the TUI mcp.json question); `Cli.cs` dispatch + help text; `ExtensionTrustPrompts.cs` and `McpConfigTrust.cs` hint wording.
- mcp.json trust hashing: the store records a hash over the raw server definitions (canonical, pre-expansion) instead of the file content — existing records re-prompt once.
- `tests/Lunate.Coding.Tests/`: new command tests (inspect / exit codes / `--yes` / `--digest` match + mismatch / `--json` / `--clear` / usage / never-loads, through `Cli.Run` with a seams store path, the existing pattern); raw-definition hash tests; e2e — after `lunate trust --yes`, a print session loads the project extension and starts the project servers; updated refusal-hint assertions.
- `README.md`: a short "trust in scripted runs" subsection.
- Specs: extensions, protocols.
- No new packages. No ADR: the trust model is unchanged (same store, same records, same hashes, explicit-only) — the command is a second approval surface for the same decision.

## Out of scope

- A path argument (`lunate trust <path>`): the command operates on the current working directory's repository; `--discover` keeps its explicit target.
- `--json` output for the command (possible follow-up; the text form is script-stable).
- Any environment variable, config key or flag that records trust implicitly — non-goal by directive.
- Trust for global extensions (they are not gated) and HTTP-transport MCP servers (none exist yet).
