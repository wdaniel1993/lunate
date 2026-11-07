## Context

ADR 0017 (addendum, skills) accepted the current index budget: a fixed 1,000 estimated-token cap, cut strictly from the end, names-only footer. With #630 moving the OpenSpec lifecycle skills into `.agents/skills/`, this repository ships 21 project skills; measured on `origin/main` (`259be474`) 14 are indexed and 7 — the alphabetical tail, five of them OpenSpec lifecycle skills — reach the model with no description. The fixed cap neither scales with the model's context window nor ranks skills, so the least-important-looking names lose the most-important descriptions.

Composition today: `SkillIndex.Compose(skills, worktreeRoot, perExtensionBudgetTokens, log)` builds the index once per session, called from `SkillCatalog.BuildIndex`, which is called from the interactive session's `ComposeSkillsAsync` and print mode before the session model resolves (`ResolveSessionModel` runs later in `ComposeAsync`).

Unattended-run note: the proposal rule "must use grill-me" is an interview skill; no user is available in this run. Its explorable-question rule was honored by code exploration; the card (#631) carries the design sign-off. Questions a user would have been asked are resolved below as Decisions 3 and 4 and in Open Questions.

## Goals / Non-Goals

**Goals:**

- Every model-visible skill is indexed with a one-line description at normal pack sizes.
- Big packs degrade gracefully: notice first (soft threshold), overflow only as a safety valve (hard ceiling).
- Skills named in `AGENTS.md` are indexed in full whenever the ceiling cuts.
- A `skills.indexBudget` setting gives users manual control.
- Replaces the fixed 1,000-token cap in `openspec/specs/skills/spec.md` ("Model exposure and budget") via a delta spec.

**Non-Goals:**

- No ranking or selection of "most important" skills beyond the `AGENTS.md` naming the repository already controls.
- No changes to discovery, trust, invocation, extension skill sources or the session format.
- No environment-variable override for `skills.indexBudget` (unlike some core keys; the card does not ask for one).
- No prompt-section rebuild mid-run.

## Decisions

1. **Budgets derive from the model's context window: soft = 2%, hard = 5%.** The window is resolved with the session model's precedence — extension-registered model declaration, else the configured catalog entry, else `CompactionReducer.DefaultContextWindow` (128,000) — read at index-build time because skills compose before `ResolveSessionModel` runs. Alternative considered: reordering composition so the session model resolves first — rejected: it would move the skills trust question and tool composition later and shift first-run error timing for no behavioural gain. For a 128,000-token default that is a 2,560-token soft threshold and a 6,400-token ceiling; the current 21-skill pack measures ~1,000 tokens and stays far below both.
2. **Per-skill trim: first sentence, still capped at 200 characters + `…`.** The description line carries the description up to its first sentence boundary (a `.`, `!` or `?` followed by whitespace and an uppercase letter, or end of text; otherwise the whole description), then the existing 200-character surrogate-safe cap applies. This keeps the existing truncation tests meaningful and keeps abbreviations (`e.g.`) from truncating early. Alternative: a smaller fixed per-skill limit — rejected: 200 is the accepted spec'd bound and enough for one line.
3. **`skills.indexBudget` semantics (the card names the setting but not the number's exact role):** a positive integer applies a single budget — skills keep their line while the index fits, overflow above it follows the hard-ceiling behaviour, and the 2%/5% context-window thresholds do not apply — because a user setting a budget has taken manual control of both thresholds' jobs. `unlimited` indexes everything at any size and emits neither the soft notice nor the hard-ceiling warning.
4. **`AGENTS.md` protection is whole-name matching.** A skill is protected when its kebab-case name appears as a word in the workspace chain's `AGENTS.md` text (the same text `AgentsInstructions.Compose` reads). The ceiling cut skips protected skills and, when every eligible skill is protected, keeps them all — the explicit naming wins over flood protection. False positives (a name appearing in prose) only extend protection one line; that is acceptable.
5. **Notices.** Below the soft threshold: silent. At/above soft with a complete index: a one-time warning-adjacent notice at composition naming the index size and pointing at disabling unused skills or packages; `/context` repeats the advice while the index stays complete. Above the hard ceiling: the standard overflow footer plus one Warning-notice naming how many skills lost their description. All numbers render with `CultureInfo.InvariantCulture`.
6. **Composed record grows** (`NotIndexed`, plus resolved thresholds, overflow count, soft-above flag) so `/context` and the notice paths derive their text from the composition instead of recomputing thresholds.

### Component view (lightweight C4-inspired; Mermaid chosen for reviewability — unattended run, assumption recorded)

```mermaid
flowchart TD
    subgraph Session composition (Lunate.Coding)
        IS[InteractiveSession.ComposeSkillsAsync / PrintMode run tips] --> SC[SkillCatalog.BuildIndex]
        SC --> SI[SkillIndex.Compose]
        SET[SettingsStore.SkillsSettings.indexBudget] --> SI
        AI[AgentsInstructions text for AGENTS.md priority] --> SI
        CW[context window: extension registry -> catalog -> 128k default] --> SI
        SI -->|Composed: text, counts, thresholds| SC
        SC --> IDX[Index recorded as skills prompt section]
        SC --> NT[one-time soft notice + overflow warning]
        SC --> CTX[/context segment with advice/]
    end
    IDX --> M[model request]
    NT --> U[user: notices]
    CTX --> U
```

- `SkillIndex` stays the single source of index text; callers only supply context (window, `AGENTS.md` text, budget override).
- Assistant session layouts unchanged; only prompt-section text, notices and `/context` wording move.

## Risks / Trade-offs

- [Catalog window can differ from the resolved session model for chat-profile routes] -> Same precedence as `AgentHarness` compaction uses; a mismatch degrades to the same default before and after.
- [Numeric `skills.indexBudget` misread as "tokens per skill"] -> Documentation renders one sentence; spec scenario states overflow begins above the total budget.
- [`AGENTS.md` frequence false positives] -> Protection only preserves a line from the ceiling cut; worst case a slightly larger index.
- [Large `skills.indexBudget` numbers re-flood small models] -> Positive integers only; clamp is not offered — the user's number is the contract.

## Migration Plan

No data migration. `scripts/docs-sync` renders the new key into `docs/configuration.md`. Rollback is reverting the commit; prompt-section text is record content, not a format change.

## Open Questions

- ADR 0017 (addendum, skills) Decision 1 records the fixed 1,000-token cap in force today; this design supersedes that detail. The adr artifact records a superseding addendum (context-window-derived budgets) rather than editing the accepted ADR.
- Should the numeric budget also raise the soft notice (advise at the same number)? Current decision 3 says no notice for a user-set budget that overflows — the overflow warning covers it.
