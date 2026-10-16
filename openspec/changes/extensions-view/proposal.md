# /extensions view: scope, state, load time

## Why

ADR-0017 and `docs/spec/extensibility.md` promise an `/extensions` view in three places ("shows
scope, state, load time"; "The `/extensions` view shows scope and state") — but no command exists.
The command table has `/quit /compact /context /mcp /trust /model /new /resume`; `/mcp` and
`/trust` cover their domains, the extensions view was never built. Found by the spec-vs-production
audit (t_298479fe, 2026-10-15).

## What changes

- **`/extensions` interactive command**: one segment per discovered extension — failed first, then
  trust-denied, then loaded — `id version (scope) state`, with loaded ones carrying load time and
  registered tool/command/hook/service counts (a field differing from the manifest reads
  `(N declared)`, a load above 500 ms reads `(slow)`), failed messages truncated to one sentence
  with a log pointer; `extensions: none configured` when empty (`extensions disabled
  (--no-extensions)` when disabled).
- **`lunate extensions [--json]` subcommand**: the same data as a text list (or JSON), composing
  the host in register-only inspect mode — trusted extensions load and register, nothing starts
  (no session events, background services or MCP), then unloads; never prompting; untrusted
  project extensions read `trust-denied`; exit 1 when any extension failed; load times labelled
  `(CLI-measured)`.
- **Load timing + per-extension states** recorded by the session at compose (loaded / trust-denied
  / failed) — the data the view needs did not exist yet.

ACP has no command surface; extension load diagnostics already flow to the host log there.
