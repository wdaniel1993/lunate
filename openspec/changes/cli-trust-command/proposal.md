# Proposal: cli-trust-command

## Why

A CI job (or any scripted run) in a repository with project extensions or a project `.lunate/mcp.json` cannot load them: print and ACP sessions refuse an untrusted project extension/config with a hint pointing at the interactive path, and the trust must have been recorded earlier by an interactive session on that machine — which CI runners never do. Daniel's directive during the tui-native-trust-prompt sign-off (2026-10-11): check whether a deliberate pre-trust path exists; if not, add one rather than reviving a console prompt. The check found none (card `t_43f9e718`).

The refusal hints therefore dead-end for exactly the audience that needs them most — scripted runs — while the trust discipline itself (one-time per repository, content-hashed, re-prompted on change, never implicit) is sound and stays.

## What

- **A `lunate trust` command** (the first subcommand; the CLI is otherwise flags):
  - `lunate trust` — inspect: prints this repository's records and every pending decision with the same content the interactive trust question shows; records nothing.
  - `lunate trust --yes` — prints the same pending decisions, then records them (project extensions and the project `mcp.json`); the CI form (`lunate trust --yes` as a workflow step before the agent run).
  - `lunate trust --clear` — clears this repository's records (parity with `/trust clear`).
  - Unknown or conflicting flags: usage on stderr, exit 2. Nothing pending: exit 0 (idempotent).
- **Discovery and hashing only**: the command never loads extension assemblies, never dispatches `ProjectTrust` hooks, and never starts an MCP server. It reuses the discovery + content-hash machinery the interactive session's pending-question count already uses (extracted into a shared inventory so the two cannot drift).
- **The hashes printed are the hashes recorded** (one evaluation per invocation), and the changed-files re-prompt wording matches the interactive question.
- **The refusal hints name both approval paths**: the extension and `mcp.json` non-interactive refusals now point at an interactive session *or* `lunate trust`.
- **Spec deltas**: extensions (the trust requirement's hint sentence + a new requirement for the command's contract); protocols (the `mcp.json` requirement's hint + read sentence, and a scenario proving the command starts nothing).

## Impact

- `src/Lunate.Coding/`: new `TrustCommand` (+ a small shared pending-trust inventory used by `InteractiveSession.Trust`); `Cli.cs` dispatch + help text; `ExtensionTrustPrompts.cs` and `McpConfigTrust.cs` hint wording.
- `tests/Lunate.Coding.Tests/`: new command tests (inspect / `--yes` / `--clear` / usage / never-loads, through `Cli.Run` with a seams store path, the existing pattern); e2e — after `lunate trust --yes`, a print session loads the project extension and starts the project servers; updated refusal-hint assertions.
- Specs: extensions, protocols.
- No new packages. No ADR: the trust model is unchanged (same store, same records, same hashes, explicit-only) — the command is a second approval surface for the same decision.

## Out of scope

- A path argument (`lunate trust <path>`): the command operates on the current working directory's repository; `--discover` keeps its explicit target.
- `--json` output for the command (possible follow-up; the text form is script-stable).
- Any environment variable, config key or flag that records trust implicitly — non-goal by directive.
- Trust for global extensions (they are not gated) and HTTP-transport MCP servers (none exist yet).
