# ADR Review Manifest

- Status: completed
- Review date: 2026-11-07

## Review Summary

ADR review completed for this change. The change diverges from an in-force ADR detail (the fixed 1,000-token index cap of ADR-0017 addendum Decision 1), so a superseding repository-level ADR was created; the change-local manifest is this file.

## In-Force ADRs Reviewed

- ADR-0017 (addendum, skills) — the index mechanics and budget order this change supersedes in part
- ADR-0016 (workspace boundary) — the read-only allowance, unchanged
- ADR-0003 (the loop owns tool calls) — untouched
- ADR-0015/0018 (session format) — prompt-section text moves, schema and golden files unchanged
- ADR-0029 (provider API families) — window resolution touches the model catalog only, not the provider contract

## New Durable ADRs Created

- `adr/0031-addendum-skills-index-budget.md` — context-window-derived soft threshold (2%) and hard ceiling (5%), first-sentence description trim, `AGENTS.md` priority over the ceiling cut, the `skills.indexBudget` override; supersedes the fixed-cap half of ADR-0017 (addendum) Decision 1
