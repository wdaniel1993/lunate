# ADR: compaction focus is a per-call argument, never session state

**Status:** Accepted with this change (maintainer sign-off requested in the PR).

## Context

`/compact` accepts optional focus instructions. The summarizer already has a fixed prompt with four sections that resume depends on. The obvious implementations are a field on the harness set before the call, or storing the text in the session's `compaction` entry.

## Decision

Focus instructions are a parameter: `CompactNowAsync(string? instructions, CancellationToken)`, threaded to `CompactionReducer.ReduceAsync(messages, instructions, ct)` and appended as a labelled block after the fixed sections. The `IChatReducer` overload passes `null`. Nothing is stored on the harness and nothing is written to the session.

## Consequences

- Automatic compaction can never inherit a focus block from an earlier manual compaction — the property falls out of the signature instead of needing a reset path.
- Resuming a session replays a summary, not the reason it looks that way; the session file stays a record of what happened, not of prompting.
- The cap (1,000 characters, refused rather than truncated) lives at the command boundary, so the agent API remains a plain string and can be driven by tests without a UI.
- A future "remember this focus" feature would need its own change and its own storage decision; this ADR deliberately does not pre-empt it.
