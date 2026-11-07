## Context

The agent loop cancels a tool run when the user presses `Esc` (ADR-0003), and the `agent-tools`
spec already demands a killed tree on cancellation. But the runner before this change started every
child with the inherited environment — the child kept the session's tty as its controlling terminal
and could read the session's keystrokes. A cancelled `bash` call whose command had started anything
interactive left that thing running and attached to the terminal: the finding in the 0.3.0 release
candidate (rc.3 finding 5) was that after `Esc` the child stayed in the process tree and the
session's prompt could not be used without a restart. The spawn path also happens to sit inside the
bash guard's `sandbox-exec` composition (ADR-0024), which was suspected of an `EFAULT` under the
deny profile — the suspicion is settled here too.

## Goals / Non-Goals

**Goals:**

- A tool child can never read the session's tty, never reconfigure its raw mode, and never outlive
  its call (cancellation, timeout both covered).
- The interactive session stays usable immediately after a cancel: prompt, input line and termios.
- Keep ADR-0024's guard exactly as accepted, including the explicit-pointer `argv`/`envp` spawn
  contract — prove the guarded path is not an `EFAULT` mechanism instead of working around it.
- Keep the never-reached-a-turn `Esc` report class answerable: a key-route trace.

**Non-Goals:**

- No change to the approval policy, the agent loop's cancellation protocol, or the bash guard's
  profile (ADR-0024 stays in force; this change only composes with its wrapper).
- No controlling-terminal Juventus fake-tty feature: children that need a terminal get an explicit
  approved call with their own pty later — out of scope here.
- No change to ACP/print mode behaviour (non-interactive runners keep their contracts).

## Decisions

- **posix_spawn with `POSIX_SPAWN_SETSID` (Unix).** One spawn call does the isolation: the flags set
  the child into a new session (no controlling terminal), file actions open stdin from `/dev/null`
  or the line-pump pipe, dup2 stdout/stderr onto captured pipes, `addchdir` to the working
  directory. The explicit `argv`/`envp` pointer arrays stay: `execvpe`-style managed marshalling is
  not documented to keep those buffers pinned through the spawn, and the pre-fix EFAULT hunt
  (macOS 26.3.1 arm64: a C repro and a .NET probe both succeed under the real deny profile) showed
  the pointer contract was never the problem. Size abuse surfaces as `E2BIG`, which the diagnostics
  name, not `EFAULT`.
- **The guard composes unchanged.** The child is still wrapped by the bash guard's resolved
  mechanism (`sandbox-exec` on macOS), but the detach happens in the spawn attributes, so the
  wrapper's argv layout and the profile are untouched. The guard probe under the deny profile
  succeeds (`UNIX guard composition` test), so no fallback and no mechanism change is needed.
- **Redirected stdio (Windows).** `Process.Start` with `UseShellExecute: false`, all three standard
  handles redirected: stdin reads EOF, output is captured, no console input handle. The tree kill
  uses `entireProcessTree: true` (job objects) — Windows is not affected by the macOS CI stop-tree
  wedge (ADR-0039 elsewhere in the tree kill history), and its path is already
  platform-mechanism-specific by ADR-0024's pattern.
- **One abstraction: `ChildProcess`.** `ProcessRunner` used to own `System.Diagnostics.Process`
  directly; now it owns a `ChildProcess` handle with `WaitForExitAsync` and `KillTreeAsync`. The
  tree kill stays in `ProcessRunner` (`ps`-walk + SIGKILL children before parents, per the existing
  ADR note about the macOS CI runner wedge) so the runner's semantics do not fork per platform.
- **`LUNATE_INPUT_TRACE` is one documented knob for the key path.** Traces live under the state root
  (default `<state root>/diagnostics/input-trace.log`), `0600`, UTF-8/LF, one line per key: kind,
  modifiers, router's decision, run/approval state, UTC timestamp. Only a path inside the state root
  is accepted (canonicalised, symlinks resolved); characters become `chars=N`, so a credential typed
  into a masked `/login` prompt or carried by a paste leaves no bytes. The routing trace point is
  the moment the router decides — before the handler switches on it.

## Risks / Trade-offs

- **Grouped children lose the controlling tty by design.** Commands that legitimately expected an
  interactive terminal (that was never supported for `bash`; commands run one non-interactive
  command) now get an honest EOF instead of reading the user's keystrokes by accident.
- **A `waitpid` poll loop instead of SIGCHLD handling.** Same latency class as the previous
  `Process`-based path; simplicity wins over signal-handler plumbing.
- **Trace cost under `LUNATE_INPUT_TRACE` off by default:** the trace point is one method call
  short-circuiting on a null sink — no hot-path change when unset.

## Migration Plan

None: internal to `Lunate.Coding`/`Lunate.Tui`, no public API change, no session-format change.
Rollback is reverting the branch.

## Open Questions

None outstanding. The unconditional `Esc`-denies on an open approval panel is tui-spec behaviour
unchanged here; cancelling the turn under an open panel remains possible through the interrupt
chord, and the trace now records which route a key actually took.
