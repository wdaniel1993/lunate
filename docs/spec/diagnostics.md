# Diagnostic bundle

`/debug [path]` writes one text file a reporter can attach to an issue without explaining their
setup. This page owns what the bundle contains, where it goes and what it deliberately leaves out.
The behaviour itself belongs to the interactive command surface in
[`openspec/specs/tui/spec.md`](../../openspec/specs/tui/spec.md); `docs/guide.md` owns the workflow.

## Destination

- Without a path the file is
  `<state root>/diagnostics/<yyyyMMdd-HHmmss-fff>-<session id>.txt`, stamped in UTC. The state root
  is `~/.lunate`, or `LUNATE_HOME` when that is set. The working directory is never used: a bundle
  written inside a repository ends up committed.
- A path argument overrides the default; a relative path resolves against the session's working
  directory. Quote a path that contains spaces.
- The command refuses to overwrite an existing file. It prints the absolute path it wrote on the
  next scrollback line.
- The file is UTF-8 without a BOM with LF line endings, so its bytes do not depend on the machine
  that produced it; on POSIX it is owner-only.

## What it contains

| Section | Fields |
| --- | --- |
| Header | The bundle format name, generation time, `ProductVersion` (the `--version` form), the .NET runtime and the platform with its architecture. |
| Session | The session file path, id, title, recorded entry count, the working directory and the git branch. |
| State root | The root path, whether `LUNATE_HOME` selected it, one line per known path (`auth.json`, `settings.json`, `models.json`, `sessions/`, `exports/`, `diagnostics/`, `extensions/`, `extensions-settings/`, `extensions-secrets/`, `cache/`, `recordings/`, `packages/`, `skills/`, `trust.json`, `trust.lock`, `history`) marked `present` or `missing`, and the top-level entry names. Only names and existence are read; no file under the root is opened. |
| Config | The resolved settings: configured model, approval policy, tool details, tool output limit, startup header, thinking settings, favourites, extra tools, subagent limits, Roslyn limit, the names of the stored auth keys (never their values), and each custom provider's id, catalog name, model and base endpoint. Endpoints are printed without userinfo or query. |
| Model | The resolved model id, provider, whether it came from the catalog or an extension, the endpoint, context window, tool and vision support, the thinking level and the last routed model. |
| Extensions | The `/extensions` rows (state, load time, contribution counts, slow hooks and remote start markers), every single-file candidate's path, state and reason, its trust digest, source digest and lock hash, the locked packages with a `direct` marker, and the dependency edges. |
| Recent events | The last 64 events, oldest first, with `t+` milliseconds since the first listed event and `Δ` milliseconds since the line above. Each line carries the event type, short run, message and call ids, tool name, stop reason, usage counts, result status and a text length — never the text itself. |

## What it never carries

- Prompt text, reasoning text, tool arguments, tool output, file contents or command output. Event
  lines measure text lengths instead of copying it.
- Credential values. The rendered document is passed through the session's one redactor before the
  file is opened, so a value Lunate holds is replaced by `[redacted:<name>]` by exact match. The
  sources and the exact-value rule are [`export.md`](export.md#credential-redaction); the bundle
  adds no pattern matching of its own.
- A credential pasted into a prompt or typed into a title is not a value Lunate stores, so it is not
  known to the redactor. The written notice says so:

  ```text
  may contain paths and configuration; review before sharing
  ```

## Where the command exists

`/debug` is interactive-only, like the other session commands. Print mode and ACP compose no command
registry: the same text is passed to the model as a prompt.
