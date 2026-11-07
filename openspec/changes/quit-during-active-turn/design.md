## Context

The interactive session routes a mid-turn Enter to the follow-up queue (`InteractiveSession.QueueFollowUpAsync`), which refuses any slash command with a "wait for the turn to finish" notice. The spec already mandates the opposite for `/quit` ("SHALL quit at any time (cancelling a running turn)"); the code disagrees. The quit machinery exists and is exercised by Ctrl+C mid-turn (`Quit()` cancels `_lifetime`; the run's cancellation token is linked to it through the key pump, so the run unwinds the same way an Esc cancel does — the harness tears down tool children) and the `InteractiveSession.RunAsync` exit path already awaits the running run before restoring the terminal.

## Goals / Non-Goals

**Goals:**

- `/quit` submitted while a turn runs cancels the turn and ends the session loop with the normal quit exit code — no refusal notice.
- Terminal restore and child teardown reuse the existing quit path; nothing new is added to the exit flow.

**Non-Goals:**

- Changing the refusal for any other slash command, or queueing slash commands generally.
- Changing the Ctrl+D behaviour (idle-only quit stays as specced).
- New process-kill machinery: the acceptance criteria's "no surviving testhost/dotnet child" is the existing cancel path's behaviour, verified in-process by tests and once manually.

## Decisions

- **Exempt `/quit` in the queue path, not a new dispatch path.** `QueueFollowUpAsync` parses the submitted line; when the command name is `/quit` it calls the session's `Quit()` instead of the refusal notice, and returns before the input pipeline (commands skip the pipeline per spec, and the turn never sees the line). Alternative considered: special-casing in the key router (a new `RoutedKey.Quit`) — rejected as a second dispatch path the single-dispatch requirement (ADR-0003 area, one command path) does not want. Alternative considered: making `BuiltinCommandSource` handle it by dropping QueueFollowUpAsync's refusal only there — wrong, because the command source is never reached while a turn runs; the queue path is the live code.
- **Why `Quit()` alone cancels the turn:** the key pump's cancellation token is `CancellationTokenSource(ct, _lifetime.Token)`, and each run's token chains through it, so `_lifetime.Cancel()` unrolls the running run exactly as an Esc cancel plus a quit would; the `RunAsync` `finally` block already awaits `_run` before the terminal restore. No extra ordering needs to be built.

## Risks / Trade-offs

- [A user presses `/quit` mid-turn expecting nothing queued] → behaviour matches the spec text, which has said this since the requirement was written; the refusal caught on review in rc.3, so the intent is uncontested.
- [The queued-command test that pinned the old refusal] → the test is replaced by the behaviour it now must pin; the refusal for the other commands stays pinned by the busy-refusal tests that never named `/quit`.

## Migration Plan

None: pure behaviour fix inside one assembly, no data or session-format change.

## Open Questions

- None open. Decisions the conventional review would normally grill on, answered from the codebase in lieu of an interviewer: (1) exit code — the normal quit code, 0, because the session was not failing; (2) bash mode is not affected — the queue path runs only in prompt mode; (3) skill invocations while busy keep their refusal (a skill starts a turn, unlike a quit).
