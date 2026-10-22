# ADR: the follow-up queue is interactive-session state, never persisted

**Status:** Accepted with this change (maintainer sign-off requested in the PR).

## Context

Queued follow-ups are messages the user has composed but not yet sent. They could be written to the session file (they are user intent) or kept in memory. They could also be modelled as a harness feature, since the harness is what knows a run is active.

## Decision

The queue lives in `InteractiveSession`, as a bounded list of the same prepared input the prompt submits. It is never written to the session file, never part of `Session`, and the agent layer does not know it exists. The drain is driven by the interactive host after a run completes.

## Consequences

- Resume cannot resurrect a queue that belonged to a process that exited; that is deliberate, because a queued message has no timestamp or turn it belonged to and replaying it would look like the user sent it twice.
- Print mode and the agent API are untouched: a queue is a property of an interactive prompt, not of the loop.
- The drain must be careful about *when* it runs — after the run task completes, not on a timer — so a cancelled turn cannot auto-launch the next message.
- If a future feature wants the queue to survive a restart, it needs its own storage decision and a replay story; this ADR deliberately does not pre-empt it.
