## Why

Lunate can start a session but cannot pick one up. The store, the interactive picker and `/new` / `/resume` exist, and `docs/guide.md` already promises `--continue` and `--resume`, yet neither flag is parsed: the CLI accepts only `-p`, `--json`, `--yolo`, `--acp`, `--version`, `--help`, `--discover` and `--no-extensions`. Sessions have no name, there is no way to ask where the current session lives or what it has consumed, and per-run choices (`model`, `thinking`, tool restriction, system-prompt overrides) exist only as saved settings. Pi parity P3 closes that gap before more surface is built on top of sessions.

## What Changes

- Add global launch flags, valid in the modes where they make sense: `--continue` / `-c`, `--resume` / `-r`, `--session <id|path>`, `--model <id>`, `--thinking <level>`, `--tools <list>`, `--no-tools`, `--no-session`, `--system-prompt <text>` and `--append-system-prompt <text>`.
- Resolution order SHALL be CLI > settings file > default; the environment keeps its existing place inside the resolved settings (CLI still wins).
- `--continue` resolves the newest session whose recorded header `cwd` equals the current working directory; the header already carries `cwd`, so no session-schema change is needed for selection.
- `--resume` reuses the interactive session picker. Print mode refuses it with a message that names the alternative (`--session` or `--continue`) instead of trying to draw a picker.
- `--session` loads one session by id or file path; `--no-session` runs without writing a session file. Selection flags are mutually exclusive with each other and with `--no-session`.
- `/name <title>` names the current session (the picker shows the title); bare `/name` reports it. The title lives in a sidecar beside the session file, so the append-only session format is untouched.
- `/session` reports the session file path, id, message count and accumulated tokens; a cost line appears only when the active model has pricing (none does today), never a fabricated `$0.00`.
- `--system-prompt` replaces the composed system prompt wholesale; `--append-system-prompt` appends after it. Both are recorded as prompt sections in the session and replayed on resume, so a resumed session keeps the caller's contract.

## Capabilities

### New Capabilities

- None.

### Modified Capabilities

- `agent-sessions`: the session store gains launch-time selection (newest by recorded working directory, by id or path, no session file), a session title recorded in a sidecar, a session report, and recorded system-prompt directives; the existing append-only format is unchanged.

## Impact

- `src/Lunate.Coding/Cli.cs`: one option parser ahead of the mode dispatch, usage and help text; `--resume`/`--session` validation per mode.
- `src/Lunate.Coding/InteractiveSession.cs` (+ `.Commands.cs`): launch selection, `/name`, `/session`, recorded directives; `PrintMode.cs` and `AcpMode.cs`: the same flags where they apply.
- `src/Lunate.Coding/SystemPrompt.cs` and `HarnessFactory.cs`: replace/append composition and tool filtering.
- `src/Lunate.Agent/Session*.cs`: store queries (newest by `cwd`, find by id) and title sidecar access; additions land in `PublicAPI.Unshipped.txt`.
- `tests/Lunate.Coding.Tests/` and `tests/Lunate.Agent.Tests/`: CLI parsing, selection, picker, command and directive tests; two existing tests change (`--unknown` handling, the exact `/help` command list).
- `docs/guide.md`: the flags paragraph and the session command list.
- No new packages, no session-schema bump, no golden-session change.
