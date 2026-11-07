## Why

The in-force tui spec says `/quit` SHALL quit at any time (cancelling a running turn), but the code routes a mid-turn submission through the follow-up queue, which refuses every slash command with "a slash command cannot be queued; wait for the turn to finish and submit it". Found while finishing #621 (rc.3 finding 5, process isolation): a user leaving a session with `/quit` while a turn runs is stuck, and can strand run-tool children instead of taking the cancel path Esc already builds.

## What Changes

- Submitting `/quit` while a turn runs cancels the running turn (the same cancellation path Esc takes: run token cancelled, tool children torn down) and ends the session loop through the normal quit path — terminal restores, exit code 0.
- Removing the mid-turn refusal for `/quit` specifically; every other slash command keeps the existing mid-turn queue refusal unchanged.
- The tui spec's Slash commands requirement gains a scenario pinning the behaviour.

## Capabilities

### New Capabilities

- _none_

### Modified Capabilities

- `tui`: the Slash commands requirement — `/quit` is exempt from the busy-session refusal and quits at any time, cancelling a running turn.

## Impact

- `src/Lunate.Coding/InteractiveSession.FollowUps.cs` (the queue's command refusal) is the only product path touched; no public API change, no new packages.
- Tests: `tests/Lunate.Coding.Tests/FollowUpQueueSessionTests.cs` (the pinned refusal test flips to the new behaviour), one updated comment in `tests/Lunate.Coding.Tests/InteractiveSessionCommandTests.cs`.
- The existing quit machinery (`Quit()`, `_lifetime`, the run await in the exit path) is reused, not extended.
