# ADR Review Manifest

- Status: completed
- Review date: 2026-10-20

## Review Summary

ADR review completed for this change. One new repository-level ADR was created: the schema-3 addendum to ADR-0018. It records the durable decisions — the image content part, the per-session content-addressed blob directory, the sweep boundary, and the image-aware request digest — and does not restate the design.

## In-Force ADRs Reviewed

- `adr/0009-memory-budgets.md` — the 5 MB image cap and the bounded hydration cache are measured against the working-set gate; no budget change is proposed.
- `adr/0012-tool-contract.md` — `ToolResult` stays the tool contract; `Images` is an additive, UI-independent field and the output text remains the model-facing carrier.
- `adr/0015-session-format.md` — schema 3 is a schema event: new golden, migration rule and ADR. The schema-3 write path uses a derived copy of `AIJsonUtilities.DefaultOptions` with one added content type; schema 1 and 2 bytes are unchanged.
- `adr/0018-session-schema-2.md` — the addendum extends it; schema 2 stays frozen and readable.
- `adr/0010-addendum-prompt-caching.md` — the cache-marker copy path must learn image content so a rolling breakpoint on an image-ending turn is not silently dropped; the digest rule keeps cache metadata out and image identity in.

## New Durable ADRs Created

- `adr/0018-addendum-images.md` — session schema 3 and image references (addendum to ADR-0018).
