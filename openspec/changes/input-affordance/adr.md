# ADR Review Manifest

- Status: completed
- Review date: 2026-10-20

## Review Summary

ADR review completed for change `input-affordance`. No new durable ADR is needed: the change extends the existing TUI palette with the theme's `user` tone and moves one width boundary, both inside the product's own TUI stack that ADR-0019 keeps in force. The palette extension reuses ADR-0019's theme rule instead of introducing a second palette or an external renderer; no architectural boundary, dependency or protocol changes.

## In-Force ADRs Reviewed

- ADR-0007 — Live-area concurrency: one renderer paints from an immutable snapshot; unchanged.
- ADR-0019 — XenoAtom.Terminal.UI not adopted; the product keeps its own TUI stack and palette (the palette extension reuses that theme rule).

## New Durable ADRs Created

- None — the rule bands and the `user` tone are presentation details recorded in `design.md` (D1, D2); they can be revisited without a new architecture.
