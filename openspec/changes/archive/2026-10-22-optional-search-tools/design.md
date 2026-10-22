# Design — optional search tools

## Common shape

Each tool follows the existing conventions exactly: a name, a JSON schema with a hand-written parser that rejects unknown keys, a read-only annotation, `ToolContext.FileChanges` untouched, and a result that names what it did. None takes approval (`read`'s class).

## `grep`

- Input: `pattern` (required, .NET regex), `path` (optional, defaults to the workspace root), `glob` (optional, e.g. `*.cs`), `case_sensitive` (default true), `max_results` (default 100, capped 500).
- Engine: the ripgrep binary when `rg` resolves on PATH (`--json` output parsed for path/line/text), else an internal walker over the file index's machinery. The fallback is not a degraded experience worth warning about — both honour `.gitignore`, skip binaries and stop at the cap.
- Output: `path:line: text`, files sorted by path then line; a trailing `[N more matches]` when the cap cut it. Never binary content: a NUL byte in the first 8 KB skips the file.
- Errors: an invalid regex is a teaching error with the engine's message; a path outside the workspace is refused like `read`.

## `find`

- Input: `pattern` (required, glob), `path`, `type` (`file` default, `dir`), `max_results` (default 100, capped 500).
- Matching is against the path relative to `path`, case-sensitive on Linux and case-insensitive elsewhere — matching the workspace's own case rules (agent-files) rather than inventing a second convention.
- Output: one path per line, sorted; directories end with `/`. A trailing `[N more files]` at the cap.

## `ls`

- Input: `path` (default `.`), `depth` (default 1, max 3).
- Output: for depth 1, `name/` for directories and `name  <bytes>` for files, directories first then files, each alphabetical. For depth > 1, an indented tree of the same form, bounded by `max_entries` (default 500, hard cap 2,000) with a truncation line.
- Hidden entries are included (`.`-prefixed); `.git` is skipped at every depth.

## Registration

`HarnessFactory` registers the extra tools only when the resolved list is non-empty. Registration is additive to the existing set and cannot override a core tool's name — a settings entry naming `bash` is an error.

## Wiring

Settings → resolved options → the factory. The precedence (CLI > env > file > default) reuses the config's existing resolution rather than a second one; the setting is read and validated with the rest, so a bad value fails at startup with the same actionable-error shape.

## Tests

- Each tool: happy path, cap, refusal outside the workspace, invalid input, `.gitignore` honoured, binary skipped (grep), depth/max behaviour (ls).
- Registration: off by default; each name registers; an unknown name errors naming the accepted values; a core-tool name is rejected; CLI overrides env overrides file.
- Both grep engines produce the same shape (a test runs the fallback directly).
