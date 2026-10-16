# Standard MCP config locations: `.agents/` + root `.mcp.json`, env expansion, per-server approval

## Why

The cross-tool conventions for MCP configuration are settling: the dotagents convention puts project servers in `.agents/mcp.json`, Claude Code uses the repo-root `.mcp.json`, and `~/.agents/mcp.json` is the user-scope sibling. Lunate reads only `~/.lunate/mcp.json` and `.lunate/mcp.json` today, so a repository already configured for the standard tools needs a duplicate Lunate-specific file. This change adopts the standard locations additively (Lunate's own files keep winning), adds Claude Code-compatible `${VAR}` expansion, and — because a repository-provided server definition runs a command — adds a per-server approval on first use on top of the existing file-level trust.

## What Changes

- **New read locations (additive).** Project scope: `.agents/mcp.json` and repo-root `.mcp.json`; user scope: `~/.agents/mcp.json`. Precedence per server name: `.lunate/mcp.json` > `.agents/mcp.json` > `.mcp.json`; user `~/.lunate/mcp.json` > `~/.agents/mcp.json`; project wins over user (unchanged). Cross-file collisions are logged naming both sources; `/mcp` keeps showing each server's source path (already spec'd).
- **Environment expansion** (Claude Code parity): `${VAR}` and `${VAR:-default}` in `command`, `args` and `env` values. A variable that is unset and has no default keeps the literal `${VAR}` text and logs a warning naming the server and the variable — the config still loads, the server still starts. The trust hash stays over raw definitions, before any expansion (already spec'd).
- **Per-server approval on first use.** Every server defined by a project-scoped file — any of the three — needs a one-time approval per definition, keyed by a canonical hash of its raw definition, with the full command shown; a changed definition re-prompts. File-level trust remains the first gate; project trust alone is no longer sufficient. Non-interactive modes refuse unapproved servers with the existing logged hint naming the approval paths.
- **`lunate trust` covers the new surface**: the three project files as pending decisions, and the per-server approvals — inspect prints them, `--yes` records them, the digest covers them, `--clear` removes them with the repository's records.
- **Never** read `.cursor/mcp.json` or `.vscode/mcp.json` (different shapes); never write to foreign config files — Lunate writes only `.lunate/mcp.json`.

## Impact

- Spec deltas: `protocols` (MCP servers from mcp.json), `extensions` (the trust command + security posture), `tui` (trust prompt).
- No new packages. The trust store gains a section (internal format); no public API change.
- Tests: parser + expansion, composition and precedence, collision logging, trust store records and hashing, prompt flow, `lunate trust` coverage, `/mcp` provenance.
