# Optional search tools: grep, find, ls

## Why

The tool set is `read`, `write`, `edit`, `bash` and the `cs_*` Roslyn tools. Searching a tree means `bash` — which is powerful but opaque: it needs a shell command, it is approved like a command, and on a large tree it is easy to write a slow one. Models that grew up with dedicated search tools write better, cheaper calls when the tools exist.

The tools are **off by default**: the current set is the product's shape, and adding three tools changes the prompt's tool list, the cost of every request and what the model chooses. Users opt in.

## What changes

Three read-only tools, each a thin, bounded wrapper:

- `grep` — pattern (regex), optional `path`, `glob`, `case_sensitive`, `max_results` (default 100, hard cap 500). Uses the ripgrep binary when present, else an internal walker; output is `path:line: text`, files sorted, no binary files.
- `find` — glob pattern, optional `path`, `max_results` (default 100, hard cap 500). Lists matching files, directories excluded unless `type: "dir"`.
- `ls` — optional `path`, `depth` (default 1, max 3). Directories first, then files, each sorted; per-entry size for files.

All three are read-only, need no approval, never follow symlinks out of the workspace, honour `.gitignore`, and refuse paths outside the workspace the same way `read` does. Their output goes through the existing truncation (and now spill) path.

## How they are enabled

- Settings: `tools.extra: ["grep", "find", "ls"]` in `~/.lunate/settings.json` (schema stays 2 — an unknown-key-free additive field with a default of empty).
- Environment: `LUNATE_EXTRA_TOOLS=grep,find,ls` overrides the file, matching the config's existing precedence.
- CLI: `--tools grep,find,ls` for a single run.

An unknown name is an error naming the accepted values. Nothing is registered when the list is empty, which is the default.

## Impact

- Change spec: `agent-tools` (the new tools), `agent-config` (the setting).
- Code: `Lunate.Coding` (the three tools, the factory's conditional registration), settings, CLI flags.
- No new packages: the internal walker is the index's existing machinery; ripgrep is used only when already installed, and its absence is not an error.
