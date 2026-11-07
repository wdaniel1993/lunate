# ADR Review Manifest

- Status: completed
- Review date: 2026-11-07

## Review Summary

ADR review completed for this change. The change implements the already-accepted loop/tool
architecture (ADR-0003, ADR-0012, ADR-0013) at the process level and composes with the accepted
bash guard (ADR-0024) and the terminal input architecture (ADR-0019); the reviewed spawn decision
stays inside those boundaries and is therefore recorded here and in the change's design and
specs rather than as a new repository-level ADR. Architecture decision sign-off for the
process-isolation contract this change pins is requested on the card's pull request (#621) as part
of this change's review.

## In-Force ADRs Reviewed

- ADR-0003 (own loop on `IChatClient`): unchanged — the cancel path the loop owns now terminates
  the child tree the same call started; no loop-contract violation.
- ADR-0012 (tool contract): unchanged — `ITool`/`ToolContext`/`ToolResult` untouched; the change
  only specifies where a tool's child process runs.
- ADR-0013 (agent loop behaviour): cancellation event/recording behaviour unchanged.
- ADR-0019 (xenoatom terminal UI): raw mode ownership stays with the TUI's `IConsoleIO` boundary;
  children can no longer reach it.
- ADR-0024 (bash guard): unchanged — the guard's mechanism, profile and probe cadence are exactly
  as accepted; the spawn below it composes via attributes, and the pointer contract stays.

## New Durable ADRs Created

- None — no new durable architectural decision: the terminal detachment is the platform
  implementation of decisions ADR-0003/0012 already made (the loop runs tools and cancellation; a
  tool call is one process with captured output and a bounded lifetime), and ADR-0024 remains in
  force as written.
