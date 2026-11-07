## Context

An interactive session enters raw mode through `Lunate.Tui`'s platform layer: `SystemConsoleIO.EnterRawMode()` → `UnixRawMode.Enter()`, which captures the termios struct as it found it and hands back an `ITerminalMode` whose `Dispose` writes the original attributes back (ADR-0019's `src/Lunate.Tui/Platform` contract). `InteractiveSession.RunAsync` holds that instance in a `using`, so `/quit`, `Ctrl+D`, exceptions and every ordinary exit path restore the terminal. Nothing handles `SIGTERM` or `SIGHUP`: the process's default disposition ends it outright and the `using` never runs, so the pty is left with echo and canonical mode off — the user runs `stty sane` (reproduced at the reported condition on the merge-base and this branch; see the PR for the `stty -a` before/after).

## Goals / Non-Goals

**Goals:**
- `SIGTERM` and `SIGHUP` mid-session (mid-turn included) restore the terminal to the pre-launch baseline before the process exits.
- One restore path: the signal handler disposes the exact `ITerminalMode` instance the session runs under, the same disposal `/quit` triggers.
- A test that drives both signals against a real pseudo-terminal and compares `stty -a` with the captured baseline.

**Non-Goals:**
- Graceful shutdown of the turn, extensions or the session record on a signal: a signal exit is abrupt by nature; this change only fixes what the terminal is left in. A session file is written incrementally during a run (ADR-0018), so nothing is lost by the abrupt exit.
- Windows console-mode restoration on signals: Windows has no `SIGTERM`/`SIGHUP` delivery to a console app like this; the platform's `Console.CancelKeyPress`/control-handler path is out of scope.
- New public API surface.

## Decisions

**D1 — `PosixSignalRegistration` with the session's raw-mode instance, installed where raw mode is won.** In `InteractiveSession.RunAsync`, after `_console.EnterRawMode()` succeeds and on non-Windows platforms, install handlers for `SIGTERM` and `SIGHUP` via `System.Runtime.InteropServices.PosixSignalRegistration`. Each handler disposes the `ITerminalMode` instance the `using` block holds — the identical object `/quit`'s scope-exit dispose releases — and then returns without cancelling, so the runtime performs the default disposition and the process terminates. Alternatives considered: (a) a separate static restore writing termios again — rejected, it is exactly the second copy of the logic the issue forbids; (b) routing the signal through the session's quit flow (`_lifetime.Cancel()`) and waiting — rejected, the main loop may sit inside a blocking read and the signal handler must not wait on it; restore-then-default keeps the exit timing of the platform.

**D2 — Make the raw-mode instance's restore safe to call from a signal handler.** `UnixRawMode.Restore` serialises `Suspend`/`Resume`/`Dispose` behind a lock, and once disposed (the terminal is back at the baseline) later `Suspend`/`Resume` become no-ops — so a signal landing while a child holds a suspended terminal still ends at the baseline, and a `Dispose` racing the session's own scope-exit is idempotent rather than two racing `tcsetattr` calls. The Windows console-mode restore gets the same treatment for symmetry of the sibling code paths.

**D3 — Handlers only when raw mode is actually entered.** `EnterRawMode` throws `InvalidOperationException` when the terminal is not usable; the catch path (fail-soft) never installs handlers. Windows is skipped explicitly because `PosixSignalRegistration` does not support `SIGHUP` there. Non-interactive runs therefore keep the platform's default signal behaviour, as the delta spec requires.

**D4 — The test drives the real binary in a pty.** A `Lunate.Coding.Tests` fixture locates the built `lunate` executable, allocates a pseudo-terminal over P/Invoke (`posix_openpt`/`grantpt`/`unlockpt`/`ptsname` — libc is already the platform bridge in `Lunate.Tui`), records the pty's `stty -a` output, launches the session against the pty (scratch `HOME`, scratch workspace, no extension files → no trust or setup prompts), waits for the input box, sends `SIGTERM` and separately `SIGHUP` to the lunate pid, and asserts the `stty -a` attribute lines equal the baseline captured before launch. For the mid-turn case the test primes a custom provider against a local `HttpListener` stub that accepts the request and stalls (tests must not use live APIs), submits a message through the pty, then signals while the turn is running. Skipped on Windows and when the binary is absent.

## Risks / Trade-offs

- [Two racing restores: the signal handler's dispose and the session's scope-exit dispose] → D2 serialises them; the mid-session end state is the baseline either way because both write the same original attributes.
- [A signal races a child tool holding the suspended terminal (editor, bash guard probe)] → once disposed, `Resume` is a no-op (D2), so the baseline cannot be overwritten after restore.
- [A handler runs on a thread-pool thread while the main thread sits in a raw read] → the handler touches only `tcsetattr` and returns; it never joins the session's locks or channel state.
- [The pty test is platform-sensitive and needs a built binary] → skip conditions (Windows, missing binary) and the test's own PTY allocation; CI's Unix legs drive it.

## Migration Plan

Behaviour-only change; no migration. Rollback is reverting the registration and the `Restore` locking.

## Open Questions

- None.
