# Proposal: Tie the ACP cancel to file round trips (T-28 review carry)

## Why

The `harden-acp-integration` review carried two timeout-edge items: a cancelled session's in-flight file call still runs its full timeout (the sync bridge passes `CancellationToken.None`), and a timed-out request is abandoned without observing its eventual failure. Both are small robustness gaps in the client-backed file seam; the permission path is already tied to the run's token.

## What changes

- The session's run state (the active run's cancellation source, the prompt count and the pending-cancel latch) moves from the private `Session` class into an internal `SessionRunState` holder created per session — shared with `ClientTextFileAccess`, so the bridge reads the live run token under the same lock. One lock keeps "cancel the active run, else latch onto the queued prompt" atomic, exactly as today.
- `ClientTextFileAccess` passes the current run token into `fs/read_text_file`/`fs/write_text_file`: a session cancel stops the in-flight file call promptly (the run ends cancelled through the harness's existing `OperationCanceledException` path); the timeout bound is unchanged.
- A timed-out request is observed explicitly (a fault-only continuation), so a late client failure cannot surface as an unobserved task exception.

## Done when

A cancel-during-file-call test proves the call stops well before its timeout; the observation is covered; the existing cancel/latch/timeout suites stay green; `scripts/verify.sh` green.
