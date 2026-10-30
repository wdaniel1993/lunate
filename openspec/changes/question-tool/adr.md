# ADR Review Manifest

- Status: completed
- Review date: 2026-10-30

## Review Summary

ADR review completed for this change. The design reuses the accepted tool, loop, TUI, session-format and ACP boundaries; it introduces no additional durable architectural decision. Maintainer sign-off is still required before implementation because the change adds a tool contract and protocol behavior.

## In-Force ADRs Reviewed

- ADR-0003 — own loop and tool execution boundary
- ADR-0007 — live-area concurrency and injected scheduler
- ADR-0012 — hand-authored tool contract and declaration-only adapter
- ADR-0013 — agent loop, event ordering, approval seam and session recording
- ADR-0015 — session format pinned by byte-for-byte goldens
- ADR-0019 — existing TUI/input stack retained
- ADR-0020 — ACP SDK and `IAcpServer` boundary

## New Durable ADRs Created

- None — no major durable architectural decisions were introduced, and no repository-level ADR files were created.
