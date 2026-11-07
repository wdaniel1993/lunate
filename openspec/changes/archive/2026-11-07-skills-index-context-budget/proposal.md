## Why

The skills index is capped at a fixed 1,000 estimated tokens and cuts strictly from the end in name order, so whichever skills sort last lose their description — this repository now ships 21 project skills (the OpenSpec lifecycle skills moved into `.agents/skills/` in #630) and 7 of them, 5 of them OpenSpec lifecycle skills, reach the model with only a bare name in the "not indexed" footer. The cut is alphabetical position, not importance, and the fixed cap does not scale with the model's context window. Issue #631.

## What Changes

- The fixed `MaxEstimatedTokens = 1000` cap is replaced by thresholds derived from the model's context window:
  - by default every model-visible skill is indexed with a one-line description, trimmed to its first sentence and capped at the existing per-skill character limit (200 + `…`);
  - a soft threshold (default 2% of the context window) only emits a one-time notice (once per composition) that the index is large — the index stays complete — and `/context` reports the index size and suggests disabling unused skills or packages;
  - a hard ceiling (default 5% of the context window) is a safety valve for small local models: above it the index overflows as today (names-only footer, cut from the end) with a visible warning naming how many skills lost their description;
  - skills named in the workspace's `AGENTS.md` are always indexed in full — the ceiling cut never drops them;
  - a `skills.indexBudget` setting (a positive integer, or `unlimited`) overrides the computed thresholds.
- The `skills` spec's "Model exposure and budget" requirement is updated accordingly (it currently mandates the fixed 1,000-token cap).

## Capabilities

### New Capabilities

(none)

### Modified Capabilities

- `skills`: the model-exposure-and-budget requirement replaces the fixed 1,000-token cap with context-window-derived soft and hard budgets, first-sentence description trimming, `AGENTS.md` priority, the one-time soft notice, the `/context` advice, the overflow warning and the `skills.indexBudget` setting.

## Impact

- `src/Lunate.Coding/SkillIndex.cs` (composition, constants, composed record), `src/Lunate.Coding/SkillCatalog.cs` (BuildIndex signature), `src/Lunate.Coding/InteractiveSession.Skills.cs` (context window, notices, `/context` segment), `src/Lunate.Coding/PrintMode.cs` (BuildIndex call), `src/Lunate.Coding/SettingsStore.cs` + `settings-schema.json` (new `skills.indexBudget` key, rendered into `docs/configuration.md` by `scripts/docs-sync`).
- No public API changes (all touched types are internal); no session-format change (prompt-section text may change, which is normal record content).
- Tests: `Lunate.Coding.Tests` (SkillIndex, SkillCatalog, settings schema, interactive/print skills composition).
