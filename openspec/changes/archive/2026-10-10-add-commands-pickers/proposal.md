# Proposal: Interactive session — commands, pickers, completion (T-22, part 2)

## Why

Part 1 wired the session (events, steering, Esc, approvals, history, E2E). Part 2 completes the guide's interactive surface: slash commands (`/model` `/new` `/resume` `/compact` `/quit`), `SelectList` model/session pickers (Ctrl+L included), `/command` Tab completion (the command list exists now — exactly why the user pinned it to part 2), the steering echo block deferred from part 1, and the interactive CLI entry that bare `lunate` still lacks. `@path` completion stays deferred to T-53.

## What changes

- **Slash commands (Coding)**: `/`-prefixed input dispatches client-side, skips the input pipeline, lands in input history; unknown commands produce a notice; `/new` `/resume` `/model` `/compact` refuse while a turn runs; `/quit` mirrors the Ctrl+C quit path and works at any time.
- **Pickers (Tui + Coding)**: a `SelectList` block in the live area (the approval-prompt pattern); the model picker (Ctrl+L or `/model`) records a model change in the session and rebuilds the harness against the same session; the session picker (`/resume`) lists the session directory and resumes the chosen session.
- **Session listing (Agent)**: header-only directory listing — newest first, bounded, corrupt-tolerant, never modifying files.
- **Tab completion (Tui + Coding)**: bare Tab routes to a new `Complete` intent; a pure `Completion` helper (full command / common prefix / candidate notice); `@path` stays T-53.
- **Steering echo (Coding)**: `SteeringInjected` commits a dim `» <text>` block to scrollback, in injection order; steering returned to the input line is never echoed.
- **Interactive entry (Coding)**: bare `lunate` starts the interactive session on a terminal; otherwise exits non-zero with a hint to use `lunate -p`; `--help` documents the entry.
- **E2E snapshot**: extended with the steering echo, Tab completion, a picker selection, `/new`, `/resume` and `/quit`; goldens regenerated deliberately.

## Done when

Per-command and per-picker tests pass; the extended E2E snapshot is green with the session-id normalization; the non-terminal hint test passes; `scripts/verify.sh` green on all four CI verify jobs; no new packages.
