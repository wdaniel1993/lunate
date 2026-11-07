# ADR Review Manifest

- Status: completed
- Review date: 2026-11-07

## Review Summary

ADR review completed for this change. It records one durable decision that revisits an in-force ADR: the skills index carries a skill's description whole up to the Agent Skills specification limit of 1,024 characters, and the limit is validation rather than a silent cut.

## In-Force ADRs Reviewed

- ADR-0003 (loop owns tools) — unchanged, respected: no loop change.
- ADR-0031 (addendum, skills index budget) — its budget decisions stay in force; this change supersedes only the description half of its Decision 1 (first-sentence trim, 200-character cap), named in ADR-0032's Supersedes field.

## New Durable ADRs Created

- `adr/0032-addendum-skill-description-spec-limit.md` — supersedes Decision 1's description half of ADR-0031; budgets and `AGENTS.md` protection unaffected.
