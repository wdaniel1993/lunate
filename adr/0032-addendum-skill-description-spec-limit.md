# ADR-0032 (addendum): Skill descriptions carry whole to the Agent Skills spec limit

Status: accepted — 2026-11-07
Supersedes: Decision 1's description half of ADR-0031 (addendum, skills index budget) — the index no longer trims a description to its first sentence or caps it at 200 characters. ADR-0031's budget decisions (soft 2%, hard 5%, `skills.indexBudget`, `AGENTS.md` protection) and every other half of ADR-0017 stay in force.

## Context

ADR-0031 kept the index compact per skill by trimming each description to its first sentence and the pre-existing 200-character cap. The description is the trigger the model matches a task against, and the "use when …" clause that decides a load is usually in the second sentence: the trim hid exactly the text a match needs. The Agent Skills specification bounds a description at 1,024 characters; the repository's own 21-skill pack with full descriptions composes to ~1,537 estimated tokens, under the 2% soft threshold of a 128,000-token window, so the whole-carry is affordable without page-budget regressions.

## Decision

1. **The index carries each description whole up to 1,024 characters** — the Agent Skills specification limit — with no sentence trim; only above that limit is the description sliced at 1,024 code units, surrogate-safe, with a trailing `…`.
2. **The limit is validation, not a silent cut**: discovery rejects a skill whose description exceeds 1,024 characters with the malformed-skill semantics (once-per-session warning, no load, no index line, no read allowance), because a description without its head or tail is no longer the declared trigger.

## Consequences

- The soft-notice and hard-ceiling budgets of ADR-0031 remain the degradation path for oversized packs; nothing in the index composition is newly fixed-size.
- `docs/` and the `skills` spec's "Model exposure and budget" and "Skill format and validation" requirements name 1,024, not 200; sessions whose model window is tighter than the pack see the existing budget machinery, not per-sentence loss.
