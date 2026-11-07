## 1. Settings surface

- [x] 1.1 Add the `skills.indexBudget` key to `src/Lunate.Coding/settings-schema.json` (positive integer or `unlimited`, user + project scope), parse it in `SettingsStore` (a `skills` section on `AgentSettings`), keep the parser↔schema tests green on both directions, and re-render `docs/configuration.md` with `scripts/docs-sync`.
- [x] 1.2 Path-resolve a budget override: a helper that turns `SkillsSettings` into (soft, hard) thresholds — a number applies a single budget, `unlimited` disables both thresholds and notices.

## 2. Index composition

- [x] 2.1 TDD: extend `SkillIndex.Compose` — first-sentence description trim (sentence boundary, then the 200-character surrogate-safe cap), context-window-derived soft/hard thresholds (2%/5%), a numeric budget override, and `AGENTS.md`-name protection in the ceiling cut; grow the `Composed` record with the resolved thresholds and what the composition dropped.
- [x] 2.2 TDD: `SkillCatalog.BuildIndex` and its two callers (`InteractiveSession`, `PrintMode`) pass the workspace chain's `AGENTS.md` text, the resolved context window at build time (extension registry → catalog → 128,000 default) and the budget override.
- [x] 2.3 TDD: the soft notice (one-time, complete index above the soft threshold) and the hard-ceiling warning (names how many skills lost their description) are emitted on session start; `/context` reports the index size and advises when the complete index measures above the soft threshold (invariant-culture numbers).

## 3. Spec, docs and validation

- [x] 3.1 Keep the repository's own 21-skill composition green through the real host: all indexed, no warning (regression test for #631).
- [ ] 3.2 Run `openspec validate skills-index-context-budget --strict`, `openspec validate --all --strict` and `bash scripts/check-openspec-skills.sh`.
- [ ] 3.3 Run the narrow gate: `dotnet build Lunate.slnx -c Release`, the touched test projects in Release, `dotnet csharpier check .` (format after the last edit).
