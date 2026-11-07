## 1. Prove the symptom (test-first)

- [x] 1.1 Pin the intended behaviour in failing tests first: a child spawned the old way owns the session tty, a detached child does not (`UnixChildProcessTests`: own pgid not the host's group, no controlling `tty` after the child's own tty check, stdin at EOF).
- [x] 1.2 Run the tmux E2E reproduction at the reported condition before the detach lands: approve a long `bash` call, cancel it, record the process tree and the `stty -a` before/during/after.

## 2. Detach the spawn (Lunate.Coding)

- [x] 2.1 Introduce `ChildProcess` (`UnixChildProcess`/`WindowsChildProcess`) and move `ProcessRunner` off `System.Diagnostics.Process` directly; Unix spawns with `POSIX_SPAWN_SETSID`, stdin from `/dev/null` or the line-pump pipe, captured stdout/stderr, `addchdir` to the working directory.
- [x] 2.2 Keep the explicit pointer `argv`/`envp` contract and surface spawn failures with the errno named (size abuse as `E2BIG`), matching the pre-existing diagnostics.
- [x] 2.3 Compose the bash guard (ADR-0024) with the detach without touching the guard's profile, wrapper layout or probe cadence; verify no mechanism change was needed by probe (macOS 26.3.1 arm64 C repro and .NET probe under the real deny profile — the `EFAULT` suspicion is dropped as not reproducible).
- [x] 2.4 Windows: redirected stdio via `Process.Start` (`UseShellExecute: false`), stdin reads EOF, tree kill stays `entireProcessTree: true`.

## 3. Kill the tree on cancel/timeout

- [x] 3.1 Route `ProcessRunner` cancellation and timeout through `ChildProcess.KillTreeAsync` (POSIX: `ps`-walk + SIGKILL children before parents; Windows: job objects) and reap; cancelled runs still drain streams and rethrow.

## 4. Key-route trace (Lunate.Tui / Lunate.Coding)

- [x] 4.1 Instrument the router's decision point with `InputTrace` behind `LUNATE_INPUT_TRACE`: kind, modifiers, routing decision, run state, UTC timestamps; refuse paths outside the state root; `0600`, UTF-8, LF.
- [x] 4.2 Keep the trace secret-safe: keys reduce to `chars=N`, pastes to a count; test the masked `/login` secret, paste-carrying-secret and escape-path-refusal cases.

## 5. Verify

- [x] 5.1 Focused suites: `UnixChildProcessTests`, `InteractiveSessionInputTraceTests`, `ProcessRunnerTests`, `InputTraceTests`, `KeyReaderTests` green in Release.
- [x] 5.2 tmux E2E with the reproducible stub model: prompt → approval `y` → child runs → `Esc` cancels, trace shows `routed=Cancel turn=True runCts=True`, the `sleep` child is gone from the process table, the prompt accepts the next command, and the `stty -a` captures compare: during == after == raw TUI state, after `/quit` == before-launch baseline.
- [x] 5.3 Fast gate: `dotnet build Lunate.slnx -c Release` (0 warnings), touched test projects green, `dotnet csharpier check .`, markdownlint clean.
- [x] 5.4 Run `openspec validate detached-tool-children --type change --strict` before archive.

## 6. Found during apply (spec-first)

- [x] 6.1 Amend the `tui` capability: the cancellable activity line keeps `esc to interrupt` in the narrow band and drops only the token deltas; the unit tests the branch already carried are confirmed against the spec and the one contradicting narrow-band verification-line test is updated; the 40×20 turn-frame goldens are regenerated deliberately and the diff (the hint line only) is shown on the PR.
- [x] 6.2 Single-source the trace path: the terminal-half trace no longer resolves `LUNATE_HOME`/the profile itself — the session resolves once through `LunateHome` and configures it before the first key is read; the state-root hygiene tests pass again.
- [x] 6.3 Bound the new test polls with named `WaitBudgets` (`ChildMarkerPoll`, `SpawnWindow`) so the wait-ceiling scan passes.
- [x] 6.4 Re-run the tmux E2E after the refactors: trace halves still write one file, cancel still kills the child tree, stty still identical before/during/after and restored on quit.
