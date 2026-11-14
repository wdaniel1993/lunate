# Command and option reference

This page answers which command and which flag a task needs, and the short answer is
`lunate --help` for the synopsis and this page for the same options with what each one does. Every
option here is compared with the `--help` output in both directions before the docs merge.

## Modes and commands

| Command | What it does |
| --- | --- |
| `lunate` | start the interactive session on a terminal ([Usage](usage.md)) |
| `lunate -p [--json] [--yolo] <prompt>` | run one prompt and print the final answer to stdout ([Print mode](print.md)) |
| `lunate --acp` | serve the Agent Client Protocol over stdio for an editor ([ACP](acp.md)) |
| `lunate --no-extensions …` | start any mode without loading extensions (their hooks, widgets and MCP servers are absent; configured `mcp.json` servers still run) |
| `lunate mcp roslyn` | serve the read-only Roslyn C# tools over stdio to another MCP client ([Roslyn MCP server](mcp-roslyn.md)) |
| `lunate --version` | print the version |
| `lunate --help` | show the synopsis and the launch options |

<!-- run: help -->
```bash
lunate --help
```

## Set up and sign in

| Command | What it does |
| --- | --- |
| `lunate setup [flags]` | the setup wizard; every answer can be a flag: `--provider --endpoint --api-family --credential-placement --models-url --credential --key-stdin --model --profile --thinking --fast --as-decision --no-test --no-model-check --force`. Absent flags skip their step; `--no-test` sends no provider request; the summary prints to stdout and never a credential ([CI](ci.md)) |
| `lunate auth list` | list providers and whether each credential comes from the environment or the auth store |
| `lunate auth login <provider>` | store a key, prompted masked on a terminal or read from a pipe with `--key-stdin` |
| `lunate auth login --custom` | add an OpenAI-compatible endpoint (id, base URL, model, key; `https` or loopback `http`) |
| `lunate auth logout <provider>` | remove a stored key |
| `lunate auth status` | report the default provider and whether a credential exists |

[Models and providers](models-and-providers.md) owns the credential flow and the custom-endpoint
validation.

## Watch extensions and trust

| Command | What it does |
| --- | --- |
| `lunate extensions [--json]` | list discovered extensions with scope, state, load time and contributions; `--json` prints a machine-readable array and exits 1 when an extension fails to load |
| `lunate extensions build <id\|path> [--json]` | compile one single-file extension now |
| `lunate extensions reload <id\|path> [--json]` | reload one extension in the running session |
| `lunate cache prune [--json]` | delete the extension builds no discovered extension points at; an installed package's entry stays, because a reinstall is its repair ([single-file extensions](extensions/single-file.md)) |
| `lunate trust` | show this repository's trust records and pending project-extension, `mcp.json` and server decisions (exit 3 when pending) |
| `lunate trust --yes [--digest <sha>] [--json] [--clear]` | record exactly what it prints — for checkouts you control, never untrusted pull requests; gate CI with `--digest <sha>`; `--clear` removes this repository's records |

[Extension lifecycle and trust](extensions/lifecycle-and-trust.md) owns what a record covers and what
it does not.

## Install packages

| Command | What it does |
| --- | --- |
| `lunate install <package-id>[@<version>] [--project] [--dry-run] [--json] [--offline] [--force] [--yes]` | download, resolve and install a package's extension and skills; `--yes` is required inside a session |
| `lunate uninstall <package-id\|extension-id> [--force] [--json]` | delete exactly what an install's record lists, and nothing else |
| `lunate packages [--json] [--verify]` | list the installed packages this working directory can see |

[Installing packages](extensions/packages.md) owns the bundle manifest, both scopes and the install
record.

## Refresh and inspect the model catalog

| Command | What it does |
| --- | --- |
| `lunate --update-catalog [--output <path>] [--source <path-or-url>]` | fetch models.dev, map it through the one catalog mapper and write the managed cache (`~/.lunate/models.catalog.json`); `--output` writes the snapshot elsewhere, `--source` reads a local snapshot for offline use |
| `lunate --discover <name-or-url>` | list an OpenAI-compatible endpoint's models and print a `models.json` draft to stdout (nothing is written) |

[The provider reference](providers.md) owns the merge order and the catalog's shape.

## Session and per-run options

These apply to the interactive session and to print mode.

| Option | What it does |
| --- | --- |
| `-c`, `--continue` | continue the newest session recorded for this working directory |
| `-r`, `--resume` | choose a session from the interactive picker; print mode cannot draw it, so use `--session` or `--continue` there |
| `--session <id\|path>` | load one session by id or file path |
| `--no-session` | run without writing a session file |
| `--model <id>` | use this model for the run (wins over settings) |
| `--thinking <level>` | `off`, `low`, `medium` or `high` for the run |
| `--tools <list>` | expose only these comma-separated tool names |
| `--extra-tools <list>` | enable optional search tools (`grep`, `find`, `ls`) |
| `--no-tools` | expose no tools for the run |
| `--max-cost <usd>` | stop the run at this cumulative session spend |
| `--max-time <duration>` | stop the run at this wall-clock limit (`30s`, `5m`, `1h`) |
| `--allow-unpriced` | allow a cost limit over a model with no known price |
| `--offline` | skip the daily update check for this run |
| `--theme <name\|path>` | paint with `default`, `ansi16` or `mono`, or read the token file at `<path>` instead of the home `theme.json` ([Themes](themes.md)) |
| `--system-prompt <text>` | replace the composed system prompt wholesale, including the tool-call conventions: the caller owns them |
| `--append-system-prompt <text>` | append a section after the composed system prompt |

The session selection flags — `--continue`, `--resume`, `--session` and `--no-session` — are mutually
exclusive with each other. In ACP the editor owns session creation, so they are usage errors there.
A launch that cannot start (unknown flags, an unknown `--model`, an unknown tool name, or a print-mode
session selection that does not resolve) exits 2.

A value may also be given as `--flag=value`, which is how a value that begins with `-` is passed.

## Related

- [Usage](usage.md) — the interactive session the options configure
- [Run one prompt and exit](print.md) — print mode's contract and exit codes
- [Run Lunate inside an editor over ACP](acp.md) — the flags ACP refuses
- [Scripted setup, trust and unattended runs](ci.md) — the same commands in a pipeline
- [Configuration](configuration.md) — the settings file behind these flags
- [Settings reference](settings.md) — every settings key, generated from the schema
