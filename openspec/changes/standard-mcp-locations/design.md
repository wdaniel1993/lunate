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
- **Identity**: a canonical hash of the server's raw definition as written (name, command, args, env key/values, cwd, timeout, exposure fields — pre-expansion), reusing the canonical-serialization machinery the file hash already uses. Machine-independent; no expanded value or secret can influence it.
- **Store**: a new section in the trust file keyed by that hash alone (not per repository — the same definition approved anywhere is the same command). The record also carries the worktree that first approved it for audit.
- **Timing**: at session compose, after file trust, before anything can start — one prompt per unapproved project server, n-of-m in the existing prompt flow. Prompt content: the server name, its source file and the **full** command (untruncated — it is what is being approved). "First use" is therefore the first session that would include the server; a definition change re-prompts.
- **Non-interactive modes** (print, ACP): refuse an unapproved server with the existing logged hint (interactive session or `lunate trust`); the rest of the session continues.
- **`lunate trust`**: the per-server approvals are pending decisions exactly like the file decisions — inspect prints them (name, source, full command), `--yes` records them, the pinned digest covers them, `--clear` removes the repository's per-server records (the definitions present in its files). No environment variable or flag can record them implicitly.

## D4 — Collisions and provenance

- Cross-file collisions: one log line per replacement naming the server and both files ("server 'x' from '…/.agents/mcp.json' replaces the same name from '~/.lunate/mcp.json'").
- `/mcp` provenance: `McpServerOptions.Source` is already the file path and `/mcp` already renders `(source)`; with five possible files each server naturally shows the file it came from. Covered by a test, not new display code.

## D5 — Non-goals

- No `.cursor/` / `.vscode/` reading, no foreign writes (explicit in the maintainer's decision).
- No HTTP/SSE transports (stdio only, unchanged).
- No change to `~/.lunate/mcp.json` / `.lunate/mcp.json` semantics beyond the uniform per-server gate and the now-logged collisions.

## D6 — Sign-off decisions (maintainer)

1. **Per-server scope** — uniform for all project files (recommended) vs new standard files only. *(recommended: uniform — "project trust alone is not enough" as a principle; the file trust stays the first gate either way)*
2. **Prompt timing** — compose-time (recommended: deterministic, testable, no mid-turn prompts) vs lazy first-start.
3. **Unset-no-default** — Claude Code parity: warn + keep literal (recommended) vs skip the server / fail the file.
4. **`~/.agents/mcp.json`** — no prompts (user-owned, recommended) vs same gate as project files.
5. **Per-server record keying** — global by definition hash (recommended, per "keyed by a hash of the server definition") vs repository-bound.
6. **`lunate trust` coverage** — per-server approvals included in inspect/`--yes`/digest/`--clear` (recommended: keeps the command the only non-interactive path for the whole surface).
