# ADR Review Manifest

- Status: completed
- Review date: 2026-10-20

## Review Summary

ADR review completed for change `tool-blocks-call-only`. No new durable ADR is needed: collapsing
a tool block to its header is a presentation default inside the product's own TUI stack, and
`toolDetails` is a settings-file value with an environment override. Neither establishes a new
architecture boundary, dependency, protocol or tool contract; both follow decisions already in
force.

## In-Force ADRs Reviewed

- ADR-0007 — Live-area concurrency: the scrollback commit still goes through the one renderer and
  the immutable frame path; unchanged.
- ADR-0012 — Tool contract: the tool result and its details are unchanged; only how much of them
  the scrollback renders changed.
- ADR-0019 — XenoAtom.Terminal.UI not adopted; the product keeps its own TUI stack and renderer
  (the collapsed header and hint stay inside it).

## New Durable ADRs Created

- None — the collapsed default, the `ctrl+o` hint rule and the `toolDetails` setting are
  presentation and configuration details recorded in `design.md` (D1–D4); they can be revisited
  without a new architecture.
