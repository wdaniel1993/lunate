# Proposal: skills-description-spec-limit

Refs #631.

## Why

The skills index carried each skill's description trimmed to its first sentence and capped at 200 characters, but the description is the trigger the model matches a task against and the "use when …" part is usually the second sentence — trimming it hides exactly the text that decides a load. The Agent Skills specification limits a description at 1,024 characters; carrying it whole to that limit is cheap (our 21-skill pack composes to ~1,537 estimated tokens, under the 2% soft threshold of a 128,000-token window) and keeps every trigger intact.

## What Changes

- The skills index carries each skill's description whole up to the Agent Skills spec limit of 1,024 characters; the first-sentence trim is removed. A description longer than 1,024 characters is sliced at that limit on a code-unit boundary that never splits a surrogate pair, with a trailing `…`.
- A skill whose description exceeds 1,024 characters is an invalid skill: discovery warns once per session, the skill is NOT loaded, is absent from the index, and leaves the read allowance — the spec limit is validation, not a silent cut.
- The six tests that pinned the first-sentence/200-character behaviour are rewritten against the new behaviour (not deleted or loosened), plus one test against the real `.agents/skills` pack asserting a complete index without a notice on a 128,000-token window.

## Capabilities

- **New Capabilities**: none.
- **Modified Capabilities**: `skills` — the index line's description clause in "Model exposure and budget" and the over-limit validation clause in "Skill format and validation".

## Impact

- `src/Lunate.Coding/SkillScanner.cs` (new validation rule), `src/Lunate.Coding/SkillIndex.cs` (description clause already landed in commit 7d4db882, kept as the safety slice).
- Tests: `tests/Lunate.Coding.Tests/SkillIndexBudgetTests.cs`, `SkillIndexTests.cs`, `SkillScannerTests.cs`.
- Documentation: `docs/guide.md` "small system prompt" line (no fixed-cap wording to change for descriptions; check only).
- Follow-up on the archived change `2026-11-07-skills-index-context-budget`, whose "Model exposure and budget" text it further modifies.
