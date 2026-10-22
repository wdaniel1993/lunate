# ADR: bash mode reuses the bash tool executor

**Status:** Accepted with this change (maintainer sign-off requested in the PR).

## Context

The prompt gains a mode that runs a typed line as a shell command. The repository already owns one shell path — `BashTool` — with pinned behaviour: shell resolution per platform, UTF-8 capture, bounded streams, a 120 s default timeout, process-tree termination on timeout or cancellation, `Execute` risk so the approval policy applies, and culture-invariant exit-code reporting. A second implementation in the input path would duplicate all six obligations.

## Decision

Bash mode invokes `BashTool` directly, with a `ToolContext` built the way the harness builds it (workspace root, session identity, cancellation token) and a synthetic call id. It is not a new tool, not a new process runner and not a new approval path.

## Consequences

- Every guarantee the `agent-tools` spec makes about `bash` applies to the mode without restating them; a change to the tool reaches the mode for free.
- The mode must render a tool block itself: the harness is not running a turn, so nothing else commits the event. The block is built through the same renderer and event types.
- The approval policy is consulted exactly once, by the tool, as it is for model-issued calls. There is no "the user typed it" bypass.
- Print mode has no input line and therefore no bash mode; the tool remains reachable there only through the model.
