# Design: skills-description-spec-limit

Refs #631. Continues the archived change `2026-11-07-skills-index-context-budget` (ADR-0031).

## Context

ADR-0031 decided (Decision 1) that the index stays complete by trimming each description to its first sentence and capping at 200 characters. A maintainer hand-off on PR #633 reversed that half-decision: the description is the trigger the model matches a task against and the "use when …" part is usually the second sentence, so trimming hides exactly the text that decides a load. Commit 7d4db882 on this branch removed the trim and raised the cap 200 → 1,024 (the Agent Skills specification's limit) but left the six tests that pinned the old behaviour red and left the spec behind the code. This change moves the spec first and then aligns code and tests.

Measured: the real 21-skill pack with full descriptions composes to 6,148 chars → ~1,537 estimated tokens — under the 2% soft threshold (2,560 tokens) of a 128,000-token window. The spec limit needs to be enforced where discovery validates, not silently sliced in composition.

## Goals

- The spec ("Model exposure and budget", "Skill format and validation") states the 1,024-character whole-carry before tests pin it.
- The six stale tests rewritten against the new behaviour (not deleted or loosened); the soft-notice and hard-ceiling paths exercised with a synthetic pack and a small window; one test against the real `.agents/skills` asserting a complete index with no notice on a 128,000-token window.
- Over-limit descriptions are an invalid-skill condition with a one-time warning, index absence and no read allowance.

## Non-Goals

- No change to budgets (2%/5%, `skills.indexBudget`), `AGENTS.md` protection, footer shape, or the skills addendum's other halves (discovery, invocation, trust).
- No surrogacy of the composition slice: the 1,024 slice stays behind validation as a defensive cap for non-scanned paths.

## Decisions

1. **Enforce the 1,024-character limit in `SkillScanner` validation**, not in composition. The scanner already owns invalid-skill semantics (frontmatter error, name mismatch, pattern): a description longer than `SkillIndex.MaxDescriptionLength` joins them, warnings once per session, no load, no index line, no read root. Composition keeps the surrogate-safe slice at 1,024 as a safety net only. Alternative — slice silently in the index — would hide the trigger's tail from the model without telling anyone; validation matches the external Agent Skills spec's contract.
2. **`SkillIndex` owns the constant.** `MaxDescriptionLength = 1024` (already in `SkillIndex.cs`) is referenced by the scanner so the spec limit has one definition.
3. **Delta mechanics.** The delta's MODIFIED blocks keep the existing scenario names (the OpenSpec tooling refuses a MODIFIED block that omits current scenarios, and no REMOVED scenario operation exists), with bodies rewritten to the new behaviour; the two scenario names that describe removed trim behaviour carry a note in their body. Disclosed on the PR rather than hand-editing specs.

## Risks / Trade-offs

- [Larger index] A pack of long descriptions grows the index up to ~5× per line vs. 200 chars -> the ADR-0031 budgets still bound it: over the soft threshold a notice fires, over the hard ceiling the cut returns. The real pack measures ~1,537 tokens, far under a 128k window's 2,560 soft threshold.
- [Packaged/extension skills also validated] A package shipping an over-limit skill now fails install-use as invalid -> correct per the Agent Skills spec; the `/skills` view shows the reason.

## Migration Plan

Spec delta first (`openspec` CLI), then scanner validation + rewritten tests in the same PR; `scripts/check-spec-archive.py` passes because the archive lands in this PR.

## Open Questions

None.
