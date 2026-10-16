# Design

## D1 — Locations and precedence

| Scope | File | Trust |
|---|---|---|
| user | `~/.lunate/mcp.json` | none (user-owned) — existing |
| user | `~/.agents/mcp.json` | none (user-owned) — new |
| project | `.lunate/mcp.json` | file trust + per-server approval — existing + new |
| project | `.agents/mcp.json` | file trust + per-server approval — new |
| project | root `.mcp.json` | file trust + per-server approval — new |

Merge by server name, later source wins within a scope; project wins over user (existing rule). Order: user files merge first (`~/.lunate/mcp.json`, then `~/.agents/mcp.json`), then project files (`.lunate/mcp.json`, then `.agents/mcp.json`, then root `.mcp.json`). Every replacement is a logged collision naming both source files and the server — the existing global-vs-project merge is silent; that changes to a log line too (same mechanism, uniform).

`.agents/` is read as a file only — never a directory walk; `.cursor/` and `.vscode/` are deliberately not read (different shapes). Nothing is ever written to the standard files.

## D2 — Environment expansion

Claude Code parity, verified against the official docs (2026-10-15):

- `${VAR}` → the value; `${VAR:-default}` → the value, or `default` when unset.
- Locations: `command`, `args`, `env` values. (Claude Code also expands `url`/`headers` for HTTP servers — Lunate is stdio-only, so those do not exist.)
- Unset without a default: the config loads, the literal `${VAR}` text is used as-is, and a warning names the server and the variable (Claude Code reports the same condition per server). No escape syntax beyond this — a `${` that does not match the pattern stays literal.
- Expansion happens at load, after parsing; the raw definitions are what the trust hashes see (already spec'd). `default` may itself be empty (`${VAR:-}`) — the documented syntax covers it.

## D3 — Per-server approval (the new gate)

- **Scope**: every server from a project-scoped file, uniformly — `.lunate/mcp.json` included (a principle, not a new-files-only carve-out: project trust alone is not enough anywhere).
- **Identity**: a canonical hash of the server's raw definition as written (name, command, args, env key/values, cwd, timeout, exposure fields — pre-expansion), reusing the canonical-serialization machinery the file hash already uses, **combined with the repository identity as the record key** — the same definition can run different code per repository (relative paths like `./mcp/index.js`; `npx` resolving a local `node_modules/.bin`), so an approval never travels between repositories (matching Claude Code's per-project approval model). Machine-independent; no expanded value or secret can influence it.
- **Store**: a new section in the trust file keyed by the repository identity and the definition hash; the record carries the worktree it was approved in for audit.
- **Timing**: at session compose, after file trust, before anything can start — one prompt per unapproved project server. Each server is its own decision, asked one at a time in the existing `n of m` flow, never one blanket keystroke for all pending servers. Prompt content: the server name, its source file and the **full** command (untruncated — it is what is being approved). "First use" is therefore the first session that would include the server; a definition change re-prompts.
- **`/mcp` surfacing**: a server whose definition references an unset variable without a default shows the missing-variable warning next to its entry; `~/.agents/mcp.json` servers show that path as their source like any other file.
- **Non-interactive modes** (print, ACP): refuse an unapproved server with the existing logged hint (interactive session or `lunate trust`); the rest of the session continues.
- **`lunate trust`**: the per-server approvals are pending decisions exactly like the file decisions — inspect prints them (name, source, full command), `--yes` records them, the pinned digest covers them, `--clear` removes the repository's per-server records (the definitions present in its files). No environment variable or flag can record them implicitly.

## D4 — Collisions and provenance

- Cross-file collisions: one log line per replacement naming the server and both files ("server 'x' from '…/.agents/mcp.json' replaces the same name from '~/.lunate/mcp.json'").
- `/mcp` provenance: `McpServerOptions.Source` is already the file path and `/mcp` already renders `(source)`; with five possible files each server naturally shows the file it came from. Covered by a test, not new display code.

## D5 — Non-goals

- No `.cursor/` / `.vscode/` reading, no foreign writes (explicit in the maintainer's decision).
- No HTTP/SSE transports (stdio only, unchanged).
- No change to `~/.lunate/mcp.json` / `.lunate/mcp.json` semantics beyond the uniform per-server gate and the now-logged collisions.

## D6 — Sign-off (Daniel, 2026-10-15)

All six answered; #5 changed, plus two additions:

1. **Scope** — uniform for all project files ✓ (file trust stays the first gate).
2. **Timing** — compose-time; start stays lazy on first use; each pending server is its own decision, asked one at a time in the `n of m` flow, not one blanket keystroke ✓.
3. **Unset-no-default** — warn + keep literal ✓; **addition: the missing-variable warning shows next to the server in `/mcp`**.
4. **`~/.agents/mcp.json`** — no prompts ✓; **addition: `/mcp` shows `~/.agents/mcp.json` as the source (other tools may write it)**.
5. **Changed: repository-bound keying** — key = repository identity + definition hash (a definition can run different code per repo: relative paths, `npx` resolving local `node_modules/.bin`; a global key would auto-approve a foreign repo's script; matches Claude Code's per-project approvals).
6. **Trust coverage** — per-server decisions included in the digest, so CI pinning catches a changed server command ✓.
