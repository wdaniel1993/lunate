# ADR Review Manifest

- Status: completed
- Review date: 2026-10-31

## Review Summary

ADR review completed for this change. The accepted ADR-0002 addendum already decides the bottom
Contracts assembly, reference directions, moved contract ownership, host bridge and forwarding policy;
this change implements that decision and introduces no separate durable architectural decision.

## In-Force ADRs Reviewed

- `adr/0002-layering-and-meai-model-types.md` — including the accepted 2026-10-31 Contracts addendum.
- `adr/0003-loop-own-vs-maf-harness.md` — the loop retains tool execution and event ownership.
- `adr/0012-tool-contract.md` — tool contracts remain Lunate-owned while MEAI supplies model types.
- `adr/0017-extensibility-architecture.md` — extensions depend on a small contract surface and nested
  work uses the host pipeline.
- `adr/0026-roslyn-built-in-boundary.md` — Roslyn projects remain on the public extension boundary.

ADR-0028 is proposed, not in force, and is not used to decide this extraction. The ADR inventory has
no `Supersedes` fields.

## New Durable ADRs Created

- None — no major architectural decision beyond the accepted ADR-0002 addendum was introduced.
