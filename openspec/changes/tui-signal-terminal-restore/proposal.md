## Why

A session killed with SIGTERM or SIGHUP exits without leaving raw mode, so the shell it was launched from is left with echo off and canonical mode off; the user has to run `stty sane` to get a usable terminal back. Found while finishing #621, and it reproduces on the merge-base, so it predates the tool-child detachment work.

## What Changes

- Register handlers for `SIGTERM` and `SIGHUP` (POSIX signal handling, not the Ctrl+C quit window) that run the same terminal-restore path `/quit` uses before the process exits.
- The restore path is reused, not duplicated: the signal path reaches the picture's existing raw-mode restore, so `/quit`, an unhandled exception and a signal all leave the terminal the way the session found it.

## Capabilities

### New Capabilities

<!-- none -->

### Modified Capabilities

- `tui`: exit paths restore the terminal to the pre-launch baseline; the signal exit paths (`SIGTERM`, `SIGHUP`) get a requirement and scenarios alongside the existing raw-mode entry rules.

## Impact

- `src/Lunate.Tui` (platform terminal mode plumbing) and `src/Lunate.Coding` (the interactive session loop that owns the raw-mode lifetime).
- `PublicAPI.Unshipped.txt` if new public API is needed; the plan keeps changes internal.
- `tests/Lunate.Coding.Tests` or `tests/Lunate.Tui.Tests`: a process-based test drives both signals against a real pseudo-terminal and compares `stty -a` with the captured baseline.
