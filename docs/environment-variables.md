# Environment variables

This page answers which environment variables Lunate reads, and the short answer is: the state root,
the settings overrides, the recording and telemetry switches, the terminal and editor variables, and
one variable per provider credential. Nothing else changes behaviour.

## State and sessions

| Variable | What it does |
| --- | --- |
| `LUNATE_HOME` | Moves the state root — sessions, `auth.json`, `trust.json`, extensions, cache and the model catalog — to an absolute path instead of `~/.lunate`. Read from the process environment only: no settings file, `AGENTS.md` or project file may supply it, because a repository that could choose the store could choose its own trust records. A root inside the workspace is refused. |
| `LUNATE_SESSION` | Set to `1` on every child the `bash` tool spawns, so a command can tell it runs inside a Lunate session. `lunate install` and `lunate trust --yes` refuse from there without `--yes`. It is a visibility marker, not a lock: a command running with your shell can remove it. |

[Security and safety](security.md) owns what the state root is and is not.

## Settings overrides

Each of these overrides the settings key of the same meaning for the process; the file stays as it
is. Values are validated like the settings file's own, so a malformed value fails the load instead of
being ignored.

| Variable | Overrides |
| --- | --- |
| `LUNATE_MODEL` | `model` |
| `LUNATE_APPROVAL` | `approval` (`ask`, `auto-edit`) |
| `LUNATE_TOOL_DETAILS` | `toolDetails` (`collapsed`, `expanded`) |
| `LUNATE_TOOL_OUTPUT_LIMIT` | `output.toolResultLimit` |
| `LUNATE_TOOL_SPILL_THRESHOLD` | `output.spillThresholdTokens` |
| `LUNATE_PROJECTION_BATCH_TURNS` | `projection.batchTurns` |
| `LUNATE_PROJECTION_MASK_TURNS` | `projection.maskTurns` |
| `LUNATE_PROJECTION_MASK_ERROR_TURNS` | `projection.maskErrorTurns` |
| `LUNATE_STARTUP_HEADER` | `startupHeader` |
| `LUNATE_UPDATE_CHECK` | `updateCheck` |
| `LUNATE_SUBAGENTS_CONCURRENCY` | `subagents.concurrency` (an integer or `unlimited`) |
| `LUNATE_SUBAGENTS_TOKEN_BUDGET` | `subagents.tokenBudget` (an integer or `unlimited`) |
| `LUNATE_SUBAGENTS_TREE_TOKEN_BUDGET` | `subagents.treeTokenBudget` (an integer or `unlimited`) |
| `LUNATE_EXTRA_TOOLS` | `tools.extra` (a comma-separated list of `grep`, `find`, `ls`) |

The launch flag wins over the environment, which wins over the file. The switch settings accept
`true`, `false`, `1` or `0`. [Settings reference](settings.md) lists every key with its type, default
and scope; [configuration](configuration.md) has the prose for each.

## Recording and replay

| Variable | What it does |
| --- | --- |
| `LUNATE_RECORD` | Set to `1` to record a run's model stream to a session recording. Recording wins over replay when both are set. |
| `LUNATE_RECORD_PATH` | Names the file the recording is written to. |
| `LUNATE_REPLAY` | Runs a session from that recording instead of calling a provider. |
| `LUNATE_DATE` | Pins the date in `yyyy-MM-dd` the system prompt is built with, so a replayed request stays byte-stable. |

## Telemetry

| Variable | What it does |
| --- | --- |
| `LUNATE_OTEL` | Set to `1` or `true` to turn the model pipeline's OpenTelemetry layer on. Off by default; prompt and tool content is not recorded. |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | Setting it also turns OpenTelemetry on, and names the endpoint the spans and metrics go to. |

## Terminal and input

| Variable | What it does |
| --- | --- |
| `LUNATE_ASCII` | Forces the ASCII glyph set, punctuation and markers. |
| `LUNATE_UNICODE` | Forces the Unicode glyph set when the detection would degrade. The overrides decide first, and because degrading is the safe direction, ASCII wins if both are set. |
| `NO_COLOR` | Disables colour everywhere. A theme can only lower the colour depth, so this wins over `tui.theme`, `--theme` and any theme file. |
| `COLORTERM` | Read with the terminal's own detection to decide the colour depth. |
| `TERM` | `dumb` degrades the glyph set to ASCII and is one reason a bare `lunate` refuses the TUI. |
| `WT_SESSION` | On Windows, marks a terminal that can render the Unicode glyph set. |
| `LUNATE_INPUT_TRACE` | `1` traces decoded key events to `diagnostics/input-trace.log` under the state root; any other value must name a path inside the state root and is refused otherwise. It records key kinds, modifiers and counts, never characters or paste content. |
| `LUNATE_KEYMAP` | Names the keymap file that overlays `keys.json`; the `--keymap` flag wins over it, and `keymap.json` in the Lunate home is the default. |

[Themes and keys](themes-and-keys.md) owns the remap rules and [terminal setup](terminal-setup.md) the
terminal matrix.

## Editor and clipboard

| Variable | What it does |
| --- | --- |
| `VISUAL` | The editor `Ctrl+G` and `/editor` launch. A `VISUAL` that cannot be launched is reported, never silently swapped for `EDITOR`. |
| `EDITOR` | The editor used when `VISUAL` is unset. |
| `SSH_CONNECTION`, `SSH_TTY` | Over ssh, `/copy` sends the OSC 52 sequence and starts no local clipboard tool. |
| `TMUX` | When set, a clipboard notice names tmux's `set-clipboard on`. |

## Packages

| Variable | What it does |
| --- | --- |
| `NUGET_PACKAGES` | The global packages folder a single-file extension's restore uses, instead of `~/.nuget/packages`. Also read when a build reports a package it cannot find. |

## Provider credentials

Every built-in provider preset declares one credential variable, and it wins over a key stored in
`auth.json`: `OPENAI_API_KEY`, `ANTHROPIC_API_KEY`, `OPENROUTER_API_KEY`, `DEEPSEEK_API_KEY`,
`MOONSHOT_API_KEY`, `ZHIPU_API_KEY`, `OPENCODE_API_KEY`, `GROQ_API_KEY`, `TOGETHER_API_KEY`,
`XAI_API_KEY` and `GOOGLE_API_KEY`. Two aliases are accepted: `OPENCODE_GO_API_KEY` for OpenCode Go
and `GEMINI_API_KEY` for Google. A custom provider entry references a variable as `env:<VARIABLE>`
instead of declaring one.

[The provider reference](providers.md) lists each preset with its kind and variable;
[models and providers](models-and-providers.md) owns where the credential lives and which source wins.

## Related

- [Configuration](configuration.md) — the settings file these variables override
- [Settings reference](settings.md) — every settings key, generated from the schema
- [The provider reference](providers.md) — each preset's adapter and credential variable
- [Security and safety](security.md) — the state root and the trust record
- [Command and option reference](cli.md) — the flags that win over both
