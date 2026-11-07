## Why

The 0.3.0 release candidate review (rc.3 finding 5) showed that cancelling a running `bash` tool call left the child attached to the terminal: after `Esc` the tool call was cancelled but the child (and everything it spawned) kept running and still held the session's tty, so the cancelled child — for example an interactive shell it had started — continued to read from and reconfigure Lunate's raw mode. The agent loop already cancels and the `agent-tools` spec already demands a killed tree on cancellation; the children were simply never started detached, and one that outlived the cancel path or reconfigured the terminal made the session unusable without a restart.

## What Changes

- Tool children start with no terminal: `bash` (and every other process a tool runs) is spawned in its own POSIX session (`POSIX_SPAWN_SETSID`) with no controlling terminal, stdin at end-of-file (`/dev/null`) or on the line-pump answer pipe when a handler answers lines, and stdout/stderr on captured pipes — a child can neither read the session's keystrokes (masked prompts included) nor open `/dev/tty` to undo the session's raw mode. Windows keeps the redirected-stdio spawn, where stdin is a pipe at end-of-file for the same reason.
- The detach is composed with the existing process-tree kill: `ProcessRunner` cancellation now runs through the shared `ChildProcess` abstraction and kills the tree (children before parents) on cancel and on timeout, so nothing spawned by a tool call outlives the approved call.
- The macOS guard composition (`sandbox-exec` wrapper) keeps its explicit-pointer `argv`/`envp` spawn contract; a probe showed the guarded path is not an `EFAULT` mechanism (macOS 26.3.1 arm64, C repro and .NET probe both succeed under the deny profile), so the guard's mechanism stays in place unchanged.
- While a turn is cancellable, the activity line keeps `esc to interrupt` visible in the narrow band (below 60 columns); only the token deltas drop there.
- `LUNATE_INPUT_TRACE` becomes the documented, secret-safe key-path diagnostic for the never-reached-a-turn `Esc` reports this class of bug produces: paths inside the state root only, 0600, one line per key naming kind, modifiers, routing decision and session state; printable input and pastes are `chars=N`, never bytes.

## Capabilities

### New Capabilities

- `process-isolation`: what tool children may reach of the terminal — no controlling terminal, redirected standard handles, stdin never a terminal, tree kill on cancel/timeout — and the diagnostic knob (`LUNATE_INPUT_TRACE`) reports that report on it.

### Modified Capabilities

- `agent-tools`: the bash-tool process contract gains the terminal detachment as part of the existing execution requirement (own session, never the session's tty, stdin at EOF or the answer pipe) alongside the already specified tree kill.
- `tui`: the cancellable activity line keeps `esc to interrupt` in the narrow band and drops only the token deltas, so the one key that stops a run stays discoverable at every width (turn-frame goldens regenerated for the 40-column frames).

## Impact

- `Lunate.Coding`: new `ChildProcess`/`UnixChildProcess`/`WindowsChildProcess` (internal implementation, what `ProcessRunner` used to do with `System.Diagnostics.Process` directly); `BashGuard` composition keeps wrapping the resolved shell.
- `Lunate.Tui`: the input trace plumbing (`InputTrace`), key router call site now instrumented.
- No public API change to the tool surface; `ProcessRunner`/`ChildProcess` are internal.
- Tests: `UnixChildProcessTests` (pgid/no-tty/stdin-EOF/guarded composition), `InteractiveSessionInputTraceTests`, `InputTraceTests`, `KeyReaderTests`.
- Docs: `docs/spec/diagnostics.md` gains the input-trace section; `docs/guide.md` names the diagnostic.
