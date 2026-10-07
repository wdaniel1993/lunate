# 0015 — Session format pinned by golden files

- Status: accepted — 2026-10-07 (maintainer sign-off; change merged)
- Date: 2026-10-06
- Relates to: ADR 0002 (layering and MEAI model types), ADR 0013 (agent loop)

## Context

Sessions are append-only JSONL and embed Microsoft.Extensions.AI types exactly as `AIJsonUtilities` serializes them (guide, "Session entries"). That makes the format depend on a third-party serializer we do not control: an MEAI update could silently change output. The format is also the resume contract and, later, the compaction record — silent drift would corrupt both.

## Decision

- The session format — envelope fields and property order, timestamp format, and the embedded `ChatMessage` serialization — is pinned by golden files in `tests/fixtures/sessions/` that must deserialize and re-serialize **byte for byte**.
- The header carries a `schema` number and the MEAI informational version (recorded, not validated).
- When a Microsoft.Extensions.AI update changes serialization output, the golden test fails: bump `schema`, add a migration that reads the old version, and record it in an ADR. Goldens are never edited silently to match new output.
- Envelope serialization uses controlled property order; the embedded message node is produced with `AIJsonUtilities.DefaultOptions`.

## Consequences

- Format drift is loud and dated: the golden test is the tripwire, and the schema number is the version line.
- Any change to the envelope or the MEAI embedding is a schema event with a migration and an ADR, not an implementation detail.
- The goldens double as format documentation and as resume-test input.
- A session file assumes a single writer (one harness or process per file); concurrent writers are not supported — T-22 keeps one harness per session.
