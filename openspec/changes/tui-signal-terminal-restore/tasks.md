---
## 1. Restore instance safety

- [ ] 1.1 Test-first: a `Lunate.Tui.Tests` unit test (Unix, fake libc-free) proves the raw-mode restore instance is safe to dispose while another thread suspends/resumes it — after dispose, `Suspend`/`Resume` become no-ops and the terminal attributes equal the baseline captured before
- [ ] 1.2 Serialise `UnixRawMode.Restore` (`Suspend`/`Resume`/`Dispose` behind one lock; dispose makes later suspend/resume no-ops) and mirror the change in `WindowsConsoleMode`'s restore for the sibling paths

## 2. Signal handlers

- [ ] 2.1 Test-first: `InteractiveSession.RunAsync` installs `POSIX` `SIGTERM` and `SIGHUP` handlers that dispose the session's raw-mode instance (observable through the fake console seam used by session tests); non-Windows only, and none installed when raw mode failed to enter
- [ ] 2.2 Implement the registration in `InteractiveSession.RunAsync` (non-Windows, after `EnterRawMode` succeeds), disposing the live `ITerminalMode` on signal and letting the platform's default disposition end the process

## 3. End-to-end signal test

- [ ] 3.1 Build the pty fixture: allocate a pseudo-terminal (libc `posix_openpt`/`grantpt`/`unlockpt`/`ptsname`), capture `stty -a` of the pty, launch the built `lunate` binary against it with a scratch `HOME`/workspace, wait for the input box
- [ ] 3.2 Drive `SIGTERM` mid-session: the test sends the signal to the lunate pid, waits for exit, and asserts the pty's `stty -a` attributes equal the captured baseline (echo, canonical mode, not re-asserted from memory)
- [ ] 3.3 Drive `SIGHUP` the same way and assert the same baseline equality
- [ ] 3.4 Drive both signals mid-turn: prime a custom provider against a local `HttpListener` stub that stalls, submit a message through the pty, and signal while the turn is running; `stty -a` equals the baseline each time

## 4. Gate

- [ ] 4.1 `dotnet csharpier format .` after the last edit; `dotnet csharpier check .` green
- [ ] 4.2 Release build and the touched test projects green (`Lunate.Tui.Tests`, `Lunate.Coding.Tests`)
- [ ] 4.3 `openspec validate tui-signal-terminal-restore --type change --strict` green, then `openspec validate --all --strict` and `bash scripts/check-openspec-skills.sh` before the archive as the final commit
