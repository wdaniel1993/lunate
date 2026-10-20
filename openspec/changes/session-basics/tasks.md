# Tasks — session-basics

Implementation runs through the `senior-dev` subagent (test-first). One commit per task, conventional-commit messages as given; `scripts/verify.sh` runs at the end of task 7 and its evidence goes in the change. No schema bump, no golden-session edits, no new packages.

## 1. Launch options and parsing

- [x] 1.1 Add `src/Lunate.Coding/LaunchOptions.cs`: the record (`Continue`, `Resume`, `Session`, `NoSession`, `Model`, `Thinking`, `Tools`, `NoTools`, `SystemPrompt`, `AppendSystemPrompt`) plus the parser, with the conflict rules from design D1 (`-c`/`--continue`, `-r`/`--resume`, `--session`, `--no-session` mutually exclusive; `--tools` vs `--no-tools`; `--thinking` via `SettingsStore.TryParseThinking`).
- [x] 1.2 `Cli.Run` parses once before mode dispatch and threads the options into `PrintModeOptions` / `InteractiveSessionOptions`; unknown flags become a usage error in every mode (update `CliTests.Run_with_unknown_arg_is_a_no_op` deliberately). `--help` lists every new flag; `--acp` rejects the selection flags (design D3).
- [x] 1.3 Tests: `CliTests` covers each flag, each conflict (exit 2, both names in the message), `--help` and `--acp` rejection. Commit: `feat(cli): parse session and prompt launch flags`.

## 2. Store selection

- [x] 2.1 `src/Lunate.Agent/Session.Selection.cs`: `FindNewest(sessionsRoot, cwd, comparison)` walking the store, header-only reads, ordering by last-modified then created then id; `FindById(sessionsRoot, id)` returning the matches; share the summary read with `Session.List`.
- [x] 2.2 `PublicAPI.Unshipped.txt` entries; no header/format change (assert a golden still round-trips byte for byte).
- [x] 2.3 Tests `tests/Lunate.Agent.Tests/SessionSelectionTests.cs`: newest wins, another cwd ignored, tie-breaks deterministic, missing directory returns null, ambiguous id names the matches. Commit: `feat(agent): select sessions by recorded working directory`.

## 3. Session selection in the frontends

- [x] 3.1 Interactive: `--continue` resolves and loads, `--session` loads by id/path, `--no-session` runs without a file, `--resume` opens the existing picker after composition; selection failures print a notice naming the input and keep the TUI usable.
- [x] 3.2 Print: `--continue`/`--session`/`--no-session` use the resolved session; `--resume` exits 2 with the D3 message before any model call.
- [x] 3.3 Tests: `InteractiveSessionPickerTests` (startup resume, picker), `CliTests` (print resume refusal, missing session), composition tests for `--continue` and `--no-session`. Commit: `feat(coding): honour --continue, --resume, --session and --no-session`.

## 4. Model, thinking and tool flags

- [x] 4.1 Resolve `--model` and `--thinking` over `SettingsStore.Resolve` (CLI > resolved settings > default) in interactive, print and ACP composition; an unknown model fails with the existing catalog message.
- [x] 4.2 `--tools <list>` / `--no-tools`: filter the composed registry by exact model-facing names after extension/MCP composition; an unknown name is a usage error listing the available names; `--no-tools` offers none.
- [x] 4.3 Tests: precedence over settings and environment, unknown model, tool filter (kept/removed names, unknown name error) in print and interactive. Commit: `feat(coding): apply model, thinking and tool flags per run`.

## 5. System-prompt directives

- [x] 5.1 `SystemPrompt.Compose(workspace, toolNames, directives)` implements replace (`--system-prompt`, no template, no tool conventions) and append (`--append-system-prompt`, after the composed prompt); no flags leaves the bytes identical to today.
- [x] 5.2 Record `system` / `system-append` as prompt sections at session creation; on resume read the latest recorded entries and apply them unless a CLI directive is present; skills keep their section and order.
- [x] 5.3 Tests: composition bytes with both flags, no-flag regression, recorded entries after a run, replay on resume, CLI-over-recorded precedence, ACP/print/interactive parity. Commit: `feat(coding): compose and record system-prompt directives`.

## 6. `/name` and `/session`

- [x] 6.1 Title sidecar `<sessionId>.title`: atomic write, trim/collapse, 120-character cap, read by id; `Session.List` labels titled rows `<title> — <id>` without changing order/bound/corrupt handling.
- [x] 6.2 `/name <title>` sets and bare `/name` reports; `/session` prints path, id, message count and input/output tokens from recorded usages, with a cost line only when pricing exists (absent today).
- [x] 6.3 Register both in `Commands.cs` (help and Tab completion follow), update the exact command string in `CliTests`, add `InteractiveSessionCommandTests`, `CommandRegistryTests`, `SessionTitleTests` and a picker label test. Commit: `feat(coding): name sessions and report their state`.

## 7. Documentation and gate

- [x] 7.1 `docs/guide.md`: the flags paragraph (`--continue`, `--resume`, `--session`, `--no-session`, `--model`, `--thinking`, `--tools`, `--no-tools`, the two prompt directives), the `/name` and `/session` commands, and the replace-warning for `--system-prompt`.
- [x] 7.2 Run `scripts/verify.sh` (build, tests, publish, budgets, public API, CSharpier, docs lint) and record the result in the change; fix only what the gate reports. Commit: `docs(coding): document session flags and commands`.

## Review remediation

The review findings were fixed after task 7, one commit each: launch usage failures now exit 2 in interactive and print (`1d9bfad`), inline `--flag=value` values (`2950e4a`), deferred `--resume` session creation (`a4aaac4`), title-free summary equality (`5527e4f`), the reparse-point-safe session walk (`49cb814`), title reads after the list cap (`38a2992`), and the `tui` slash-command delta (`bc7fd91`); the guide/design note is `0b849ea`. Deliberate change: print-mode selection failures and an unknown `--model` moved from exit 1 to 2 per design D1; a settings-only unknown model stays 1, and interactive composition failures now exit 1 instead of 0.

Gate (2026-10-20, full `scripts/verify.sh` on code head `0b849ea`; this note is the only change after it): `verify: OK` — 4,045 tests passed in both the default and the de-AT culture pass, path-index budget passed (2 tests), single-file publish succeeded, `startup median: 59 ms (budget 150 ms)`, CSharpier clean (1,099 files), style and analyzers clean, docs lint clean (53 files, 0 issues), public API unchanged. `openspec validate session-basics --strict` is valid.
