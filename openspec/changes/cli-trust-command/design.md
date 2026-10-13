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

- `lunate trust` (no flags) — prints the repository's records (short hashes, the `/trust` line format), every **pending** decision with the same content the interactive question shows, and **one digest over all pending decisions** (D8). Records nothing. Exit 0 when nothing is pending, **3 when pending decisions exist** (usable as a CI check).
- `lunate trust --yes` — prints the same pending decisions, records them (extension section and mcp.json section), and prints what was recorded. Idempotent: a second run finds nothing pending and exits 0. With `--digest <sha>` it records only on a match (D8).
- `lunate trust --clear` — clears the repository's records from both sections (the store's `ClearRepository`) and **prints what it removed** (or that there was nothing to clear).

Exit codes: **0** success (inspect: nothing pending; `--yes`: recorded or already trusted; `--clear`: cleared) · **1** unexpected failure (the `--discover` pattern) · **2** usage (unknown flags, `--yes` with `--clear`, `--json` with `--yes`/`--clear`) · **3** inspect found pending decisions · **4** digest mismatch (nothing recorded, D8).

### D4 — What `--yes` records: everything a session would

A **pending** decision is `ExtensionTrustDecision.Prompt` (unknown hash or changed hash) — exactly what an interactive session would ask. A **new worktree** of a trusted repository is not a pending decision (sessions record it without asking); `--yes` records it too, so a scripted run ends in the same state a session would reach. Nothing else is recorded; a trusted hash is left alone.

The printout is the decision's evidence: hashes are computed once per invocation and the recorded values are the printed ones.

### D5 — Discovery and hashing only; the command executes nothing

The command uses descriptor discovery + content hashing (no loader `Load`, no assembly resolution, no `ProjectTrust` dispatch, no MCP server start; the project `mcp.json` is parsed to name its servers in the printout — parsing is not starting). A test proves it with an extension whose factory would write a marker file: after `lunate trust --yes` the records exist and the marker does not. This keeps the command's blast radius at "writes trust.json" — the security-review surface stays the same as the store's.

### D6 — Security posture

Explicit-only, per the directive: no environment variable, config key, or flag records trust implicitly, and there is no console prompt. The command prints exactly what it trusts before recording. Denial semantics are unchanged (the command cannot "deny" — it either records the pending set or does nothing). The refusal hints keep deny-by-default in sessions: they now name both approval paths, but nothing approves on their behalf.

### D7 — No ADR

The trust model is unchanged: same store, same repository/worktree/hash keys, same sections, same explicit decision. The command is an additional approval surface, not a model change. Recorded here; the maintainer can overrule at sign-off.

### D8 — Digest pinning is the CI form

Inspect prints one digest over all pending decisions. The digest is SHA-256 over a canonical, deterministic serialization of the pending content only — per extension the kind, id, version, changed flag and content hash; per `mcp.json` the kind, raw server definitions and content hash — sorted deterministically, invariant culture. Repository identity, worktree path, extension directory and config path never enter it, so the digest is machine-independent: a digest pinned on a reviewer's checkout matches the same pending content on a CI runner's checkout. It appears only when something is pending (nothing to pin otherwise; `--json` carries `null`; the `--json` pending entries keep the human-useful directory and path).

`lunate trust --yes --digest <sha>` recomputes the digest and records **only on an exact match**. On mismatch: nothing is recorded, the exit code is 4, and the command shows the difference — the pinned digest, the computed digest, and the current pending set (the full current evidence; a whole-set digest cannot localize per item, and the pending list is what an operator needs to see what changed). With a pin and nothing pending the pin cannot be verified: nothing is recorded, the command prints `trust: nothing is pending; the pinned digest cannot be verified` with the pinned digest, and exits 4 (plain `--yes` with nothing pending stays exit 0). The pinning flow lets CI verify that what it is about to trust is exactly what a human reviewed: `lunate trust --json` on the reviewed revision, pin the digest, then `--yes --digest` in the job.

Plain `--yes` remains for local scripts; the docs mark it "only for checkouts you control" (never on untrusted PRs/forks). Pinning does not weaken `--yes` — both record the same explicit decision — but the digest gate binds the recorded set to a reviewed one.

### D9 — `--json` is the inspect output contract

`lunate trust --json` prints one JSON document on stdout — `records`, `pending`, and `digest` — for scripts. Canonical conventions: `\n` line endings, invariant culture, deterministic order (the same sort as the digest). `--json` with `--yes` or `--clear` is a usage error (2): the flag describes inspection, and recording output stays human-readable. The text form is not a scripting contract; the JSON is.

### D10 — The mcp.json trust hash is over raw server definitions

The project file's trust hash changes from the file-content hash to a hash over **each server's raw definition as parsed from the file, before any `${VAR}` expansion** — name, command, args, env, cwd, call timeout as written (unknown fields excluded), canonically serialized and name-sorted, invariant culture. Consequences: machine-independent (no expanded environment values can enter it), no secret value can influence it, and formatting or key-order changes no longer re-prompt. `${VAR}` expansion does not exist in the loader today; the guarantee binds any future expansion (the hash is computed at trust time from the file as written, expansion being a use-time concern).

Existing mcp.json records are keyed by the old hash, so they re-prompt once after this change — acceptable pre-1.0 (the store is per-user and local). Extension directory hashing is unchanged.

### D11 — `--clear` prints what it removed

The removed record lines (the `/trust` format) or an explicit "no records to clear". Clear stays silent about other repositories, matching the store's `ClearRepository`.

### D12 — The printout reuses the interactive question's content

Extension: `Load project extension '{id}' {version} from {directory}`, plus the changed-files sentence when re-prompting. mcp.json: `Start {n} {server|servers} from project mcp.json '{path}'` with one `{name} — {truncated command}` detail line per server, truncated at the same length the TUI uses. The TUI prompt's command/truncate helpers move to a shared internal helper so the two surfaces cannot drift.

### D13 — Docs

The README gains a short "trust in scripted runs" subsection (under Quick start): records live in `~/.lunate/trust.json` per user and machine, so ephemeral CI runners run the trust step in every job; `lunate trust` / `--yes` / `--digest` usage; the `--yes` warning (only for checkouts you control — never on untrusted PRs/forks). The help text carries the one-line version.

## Deviations

None yet.

## Test plan

- `TrustCommandTests` (new; in-process `Cli.Run` with `ExtensionSeams { StorePath, Log }`, the `CliExtensionStoreTests` pattern): inspect prints pending and records nothing; inspect exits 3 with pending, 0 without; `--yes` records both sections with the printed hashes; second `--yes` is a no-op success; `--clear` removes both sections and prints the removed lines; `--yes --clear`, `--json --yes`, unknown flags → exit 2; changed-files re-prompt wording appears when a recorded hash differs; new-worktree entry recorded by `--yes`.
- Digest: inspect prints the digest; `--yes --digest` with the printed value records; with a wrong value records nothing, exits 4, prints both digests and the pending set; the digest changes when any pending content changes (e.g. an edit to the extension directory) and is stable across invocations and formatting-only edits.
- `--json`: one document, canonical `\n`, deterministic order; `digest` null when nothing is pending; parseable by `System.Text.Json` in tests.
- Raw-definition hashing: two files with identical server definitions but different formatting/key order produce the same hash; different env variable *values* in the environment do not change it; a secret value never appears in trust.json.
- Never-loads proof: a test extension whose factory writes a marker; `lunate trust --yes` records without the marker appearing.
- E2E (print mode): an untrusted repository with a project extension refuses with the hint naming `lunate trust`; after `lunate trust --yes` (same store), the print session loads the extension (marker present) and no hint is logged. Same for a project `mcp.json` (server starts after trust; the refusal hint names both paths; inspect starts nothing).
- Existing refusal-hint assertions updated (`ExtensionTrustPromptTests`, `McpConfigTrustTests` or whichever assert the exact sentence).
- The suite's process-spawning classes keep their `mcp-processes` collection; new tests that spawn (the mcp.json e2e) join it.
