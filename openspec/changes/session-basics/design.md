## Context

Today `Cli.Run` special-cases the mode first: `--version`, `--help`, `trust`, `extensions`, `--discover`, `--acp`, then print mode if `-p`/`--print` is present, otherwise interactive. The print parser rejects every flag-shaped token it does not know, and the interactive path takes no arguments at all. The building blocks this change needs already exist:

- `Session` writes a header with `id`, `schema`, `cwd`, `repo` and `worktree`; `Session.List` returns the newest 20 summaries per directory, sorted for the picker.
- `SessionPaths` groups a store by repository/worktree (or plain cwd hash); `HarnessFactory.DefaultSessionDirectory` picks the directory, `HarnessFactory.CreateSession` creates and names the file.
- `InteractiveSession.OpenSessionPicker` reuses `Session.List`; `ResumeSession` loads a file and recomposes.
- `SettingsStore.Resolve` already owns model, thinking and the environment-over-file precedence, and `SettingsStore.TryParseThinking` is the one level table.
- System prompt sections are first-class: `AgentHarnessOptions.SystemPrompt` plus `PromptSections`, and the session records sections with `AppendPromptSection` (skills already do this).
- `AgentHarnessOptions.Session` is nullable, so "no session file" needs no core change.

What is actually new: one launch-options parser, store queries (newest-by-cwd, find-by-id), title storage, the two commands, directive composition/replay, a tool filter, and wiring in three frontends.

## Goals / Non-Goals

**Goals:**

- The flags behave the same in interactive and print mode where the concept exists; ACP gets the composition flags only.
- One precedence rule everywhere: CLI > resolved settings (file + environment) > default.
- A resumed session is reproducible: the title, the recorded directives and the chosen message history come back.
- No session-format change and no schema bump.

**Non-Goals:**

- Pricing data for models (the `/session` cost line stays absent until the catalog has pricing).
- Session deletion, renaming outside `/name`, or exporting.
- Changing `Session`'s append-only format, the golden files or the schema.
- Editor-driven session lifecycle in ACP (`session/new`, `session/load` stay the editor's contract).

## Decisions

### D1 — One launch-options parser ahead of the mode dispatch

A new `LaunchOptions` record (`src/Lunate.Coding/LaunchOptions.cs`) is parsed once in `Cli.Run` before mode dispatch. It carries `Continue`, `Resume`, `Session`, `NoSession`, `Model`, `Thinking`, `Tools`, `NoTools`, `SystemPrompt`, `AppendSystemPrompt`, plus the existing print flags. Parsing rules:

- Flags take the next token as their value (`--session abc`, `--model gpt-4o`); `--flag=value` is not accepted in this change (kept as an open question).
- `-c`/`--continue`, `-r`/`--resume`, `--session` are mutually exclusive; each is also mutually exclusive with `--no-session`. A conflict is a usage error (exit 2) naming both flags.
- `--tools` takes a comma-separated list of exact model-facing names; `--no-tools` means the empty list and conflicts with `--tools`.
- Unknown flags stay a usage error in print mode (today) and become a usage error at launch in every mode; `CliTests.Run_with_unknown_arg_is_a_no_op` is updated deliberately.
- `--thinking` reuses `SettingsStore.TryParseThinking`, so the vocabulary stays one table.

Files: `src/Lunate.Coding/Cli.cs` (parse, dispatch, help/usage), `LaunchOptions.cs`, `tests/Lunate.Coding.Tests/CliTests.cs`. `PrintModeOptions` and `InteractiveSessionOptions` gain the resolved fields; the parser resolves `Model` and `Thinking` against settings inside each frontend, not in the parser.

### D2 — `--continue` resolves by the recorded header `cwd`

The header already records `cwd` (`SessionHeaderEntry.Cwd`), so nothing about the format changes. New store query in `src/Lunate.Agent/Session.Selection.cs`:

```
SessionSummary? FindNewest(string sessionsRoot, string cwd, StringComparison comparison)
```

It walks every `*.jsonl` under the sessions root (bounded header read only), compares the header's `cwd` to the canonicalized current working directory with the platform comparison the workspace uses (ordinal on Linux, ordinal-ignore-case elsewhere), and returns the newest by last-modified, then creation, then id — the same tie-breaks as `Session.List`. Walking the whole root, rather than only `DefaultSessionDirectory`, keeps the flag correct after a worktree move or a hash-folder rename; the per-directory folder is still what gets listed in the picker.

No session for the directory is an error: `lunate: no session for '<cwd>'; run a prompt to start one`. The query is O(files) header reads and stays out of the startup path (it runs only when `--continue` is given). The schema impact is **none**; `repo`/`worktree` are not needed for the comparison.

Files: `src/Lunate.Agent/Session.Selection.cs`, `Session.List.cs` (shared summary read), `tests/Lunate.Agent.Tests/SessionSelectionTests.cs`, `PublicAPI.Unshipped.txt`.

### D3 — `--resume` is the existing picker; print mode refuses it

Interactive: `InteractiveSessionOptions.StartSession` (path or null) and `StartWithPicker` (bool). When `StartWithPicker` is set the session starts as today and the picker opens after composition, reusing `OpenSessionPicker` unchanged. When `StartSession` names a path the session starts from that file instead of creating one.

Print: `--resume` fails before any model call with exit 2 and

```
lunate: --resume needs the interactive picker; use --session <id|path> or --continue
```

ACP: `--continue`, `--resume`, `--session` and `--no-session` are usage errors (the editor owns that lifecycle); the parser reports them together with `--acp`.

`--session <value>`: a value containing a path separator or ending in `.jsonl` is a path; otherwise it is an id, resolved as `<id>.jsonl` across the store (ambigity is an error naming the matches). A missing file fails with the path in the message.

Files: `src/Lunate.Coding/InteractiveSession.cs`, `InteractiveSession.Commands.cs`, `PrintMode.cs`, `AcpMode.cs`, `Cli.cs`, tests in `InteractiveSessionPickerTests.cs` and `CliTests.cs`.

### D4 — Title sidecar, `/name` and `/session`

`/name <title>` stores the title in `<sessionId>.title` beside the session file: one UTF-8 line, trimmed, interior whitespace collapsed, at most 120 characters; empty or longer is a dim notice, not a crash. Write is atomic (temp file + move) and never touches the JSONL. Bare `/name` prints `session name: <title>` or `session name: (none)`. `Session.List` gains an optional title lookup so the picker labels rows `<title> — <id>` (unlabelled rows stay `<id>`), without changing ordering or the 20-entry bound. Rationale: a title entry would change the append-only format and force schema 3 plus new goldens; a sidecar keeps the format guard and older builds untouched. Alternative rejected: a `title` entry (schema 3) — correct long-term, too large for this change.

`/session` is read-only and prints one block: `session: <path>`, `id: <id>`, `messages: <n>`, `tokens: <input> in / <output> out`. Token totals come from the session's message usages, so they survive a resume. A `cost:` line is printed only when the active model exposes pricing; `ModelInfo` has no pricing field today, so the line is absent (never `$0.00`). Both commands join the registry in `Commands.cs`, which makes `/help`, Tab completion and the "every dispatchable command has a descriptor" test pick them up.

Files: `src/Lunate.Agent/SessionTitle.cs` (or `Session.Selection.cs`), `Session.List.cs`, `src/Lunate.Coding/Commands.cs`, `InteractiveSession.Commands.cs`, tests `SessionTitleTests.cs`, `InteractiveSessionCommandTests.cs`, `CommandRegistryTests.cs`, `InteractiveSessionPickerTests.cs`.

### D5 — System-prompt directives: replace, append, record, replay

Composition stays in one helper:

```
SystemPrompt.Compose(workspace, toolNames, LaunchOptions? directives)
```

- `--system-prompt <text>` replaces the composed prompt wholesale. This is the caller's contract: the tool-call conventions in `system-prompt.md` are **not** injected, and the model may not call tools correctly; the help text says so.
- `--append-system-prompt <text>` keeps the composed prompt and appends the text as a separate section after it.
- Without either flag composition is byte-identical to today.

Recording reuses `SessionPromptSectionEntry`, so no schema bump: at session creation the frontend appends section `system` with the replaced text (only when `--system-prompt` was given) and section `system-append` with the appended text (only when given). On resume the session's latest `system`/`system-append` entries are read back and applied unless a CLI directive overrides them (CLI > recorded > composed). The skills section keeps its name and ordering after the directives.

Files: `src/Lunate.Coding/SystemPrompt.cs`, `HarnessFactory.cs` (shared composition), `InteractiveSession.cs`, `PrintMode.cs`, `AcpMode.cs`, tests `SystemPromptHarnessTests.cs`, `PrintModeTests.cs`, `InteractiveSessionResumeTests.cs`.

### Structure

```mermaid
flowchart LR
    CLI[Cli.Run / LaunchOptions] -->|interactive| IS[InteractiveSession]
    CLI -->|print| PM[PrintMode]
    CLI -->|acp| ACP[AcpMode]
    IS --> SEL[Session selection]
    PM --> SEL
    SEL --> STORE[(~/.lunate/sessions)]
    IS --> SP[SystemPrompt.Compose]
    PM --> SP
    ACP --> SP
    IS --> CMD[/name /session]
    CMD --> STORE
```

- One parser owns flag meaning; the three frontends only consume resolved options.
- Selection is a store query; the picker and `--continue` share the summary read and ordering.
- Directives and titles belong to the store; no frontend invents a second representation.
- Assumption: the editor owns session lifecycle in ACP, so selection flags stop at the parser there.

## Risks / Trade-offs

- [Title sidecar drifts from the session file] → title is written atomically and read only by id; a missing sidecar means "no title", never an error; deleting a session may leave a sidecar, harmless and documented.
- [Scanning the store for `--continue` is O(all sessions)] → header-only reads, bounded listing cap per directory, run only when the flag is present; the startup budget is untouched.
- [`--system-prompt` without tool conventions degrades tool use] → documented in help and design; it is the caller's explicit contract, not the default.
- [Strict parsing breaks `--unknown` no-op behaviour] → intended; the test is updated and the usage message names the flag.
- [Help test asserts an exact command string] → update it together with the command registry in the same task.
- [ACP flag rejections surprise editor integrations] → only new flags are rejected; existing ACP invocation is unchanged.

## Migration Plan

Additive. Existing invocations (`lunate`, `lunate -p ...`, `lunate --acp`, `trust`, `extensions`) keep their behaviour; `--session`/`--continue` load old schema 1/2 files as today. Rollback is deleting the flags; session files and sidecars stay readable.

## Open Questions

- Accept `--flag=value` as well as `--flag value`? (Deferred; space-separated only in this change.)
- Should `--continue` also consider sessions whose header `cwd` is a parent of the current directory (running from a subdirectory)? Current answer: no, exact equality.
- Pricing: add optional input/output pricing to `ModelInfo` (AI layer, catalog schema, public API) in a follow-up so `/session` can print cost? This change prints tokens only.
- ACP: should a future `session/load` mapping reuse the store selection, or stay entirely editor-driven?
- Spec placement: the modified requirements are `agent-sessions` and the `tui` slash-command requirement; `/name` and `/session` are TUI commands whose state is session state, so the `tui` delta only lists them in the command set and leaves the behaviour to `agent-sessions`.
