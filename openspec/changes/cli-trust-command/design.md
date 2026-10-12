# Design: cli-trust-command

## Context

Trust today: `ExtensionTrustStore` (`~/.lunate/trust.json`) records, per repository identity (`GitCommonDir ?? WorktreeRoot`) and worktree path, a SHA-256 content hash — one section for project extensions (directory hash), one for the project `.lunate/mcp.json` (file hash). Sessions evaluate, prompt when the hash is unknown or changed, and record on approval; new worktrees of a trusted repository record automatically. Interactive sessions ask in the live area (`TuiExtensionTrustPrompt`, `TuiMcpConfigTrustPrompt`); print and ACP sessions use `NonInteractiveTrustPrompt` / `NonInteractiveMcpConfigTrustPrompt`, which refuse and log a hint. Nothing else can record trust — the gap this change closes.

The interactive session's pending-question count (`InteractiveSession.CountPendingTrustQuestions`) already performs exactly the discovery + evaluation the command needs (descriptor discovery, content hashing, mcp.json hashing — no assembly load). That logic moves into a shared inventory so the count and the command cannot drift.

## Decisions

### D1 — `lunate trust` is a subcommand, not a flag

The CLI is flags (`--version`, `--help`, `--discover`, `--acp`, `-p`) with no subcommands; `trust` becomes the first. Rationale: it is an action with its own flags and a natural name pairing with the TUI's `/trust`; `lunate trust --yes` reads as what CI should run, where `lunate --trust --yes` reads as a mode. The alternative (flag) was rejected on readability and because the command is expected to grow flags; the cost is one dispatch branch (`args[0] == "trust"`), matching how `--discover` is special-cased today. Help text gains its own usage block.

### D2 — The command operates on the current working directory's repository

`new Workspace(Environment.CurrentDirectory)` provides `WorktreeRoot` and identity (`GitCommonDir ?? WorktreeRoot`) — the same values a session in that directory would use. No path argument in v1: CI runs in the checkout root; `--discover` keeps its explicit target. A non-git directory works (identity falls back to the worktree root), matching session behavior.

### D3 — Inspect, approve, clear — three explicit verbs

- `lunate trust` (no flags) — prints the repository's records (short hashes, the `/trust` line format) and, for every **pending** decision, the same content the interactive question shows — extension: `Load project extension '{id}' {version} from {directory}`, plus the changed-files sentence when re-prompting; mcp.json: `Start {n} {server|servers} from project mcp.json '{path}'` with one `{name} — {truncated command}` detail line per server — then a hint to run `lunate trust --yes`. Records nothing.
- `lunate trust --yes` — prints the same pending decisions, records them (extension section and mcp.json section), and prints what was recorded. Idempotent: a second run finds nothing pending and exits 0.
- `lunate trust --clear` — clears the repository's records from both sections (the store's `ClearRepository`), prints the outcome.

`--yes` and `--clear` together, or any unknown flag, print usage on stderr and exit 2. Unexpected failures exit 1 (the `--discover` pattern); nothing pending or already trusted exits 0.

### D4 — What `--yes` records: everything a session would

A **pending** decision is `ExtensionTrustDecision.Prompt` (unknown hash or changed hash) — exactly what an interactive session would ask. A **new worktree** of a trusted repository is not a pending decision (sessions record it without asking); `--yes` records it too, so a scripted run ends in the same state a session would reach. Nothing else is recorded; a trusted hash is left alone.

The printout is the decision's evidence: hashes are computed once per invocation and the recorded values are the printed ones.

### D5 — Discovery and hashing only; the command executes nothing

The command uses descriptor discovery + content hashing (no loader `Load`, no assembly resolution, no `ProjectTrust` dispatch, no MCP server start; the project `mcp.json` is parsed to name its servers in the printout — parsing is not starting). A test proves it with an extension whose factory would write a marker file: after `lunate trust --yes` the records exist and the marker does not. This keeps the command's blast radius at "writes trust.json" — the security-review surface stays the same as the store's.

### D6 — Security posture

Explicit-only, per the directive: no environment variable, config key, or flag records trust implicitly, and there is no console prompt. The command prints exactly what it trusts before recording. Denial semantics are unchanged (the command cannot "deny" — it either records the pending set or does nothing). The refusal hints keep deny-by-default in sessions: they now name both approval paths, but nothing approves on their behalf.

### D7 — No ADR

The trust model is unchanged: same store, same repository/worktree/hash keys, same sections, same explicit decision. The command is an additional approval surface, not a model change. Recorded here; the maintainer can overrule at sign-off.

## Deviations

None yet.

## Test plan

- `TrustCommandTests` (new; in-process `Cli.Run` with `ExtensionSeams { StorePath, Log }`, the `CliExtensionStoreTests` pattern): inspect prints pending and records nothing (trust.json absent or unchanged); `--yes` records both sections with the printed hashes; second `--yes` is a no-op success; `--clear` removes both sections; `--yes --clear` and unknown flags exit 2 with usage on stderr; changed-files re-prompt wording appears when a recorded hash differs.
- Never-loads proof: a test extension whose factory writes a marker; `lunate trust --yes` records without the marker appearing.
- E2E (print mode): an untrusted repository with a project extension refuses with the hint naming `lunate trust`; after `lunate trust --yes` (same store), the print session loads the extension (marker present) and no hint is logged. Same for a project `mcp.json` (server starts after trust; the refusal hint names both paths).
- Existing refusal-hint assertions updated (`ExtensionTrustPromptTests`, `McpConfigTrustTests` or whichever assert the exact sentence).
- The suite's process-spawning classes keep their `mcp-processes` collection; new tests that spawn (the mcp.json e2e) join it.
