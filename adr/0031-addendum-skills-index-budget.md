# ADR-0031 (addendum): The skills index budget scales with the model's context window

Status: accepted — 2026-11-07
Supersedes: the fixed-index-cap half of ADR-0017 (addendum, skills) Decision 1 — the index is no longer "capped at 1,000 estimated tokens". Discovery, invocation, trust, extension sources and the session format of that ADR are unchanged.

## Context

ADR-0017 (addendum) capped the skills index at a fixed 1,000 estimated tokens, cut strictly from the end in name order, with a names-only footer. Repository #630 moved the OpenSpec lifecycle skills into `.agents/skills/`, taking this repository from 9 to 21 project skills: 14 indexed, 7 overflowed to bare names (issue #631). The cut is alphabetical position, so whichever names sort last — including five of the seven lifecycle skills a session must pick correctly — reach the model without descriptions, and the fixed number neither scales with the model's context window nor yields to an explicit instruction.

## Decision

1. **The index stays complete by default.** Every model-visible skill keeps its full index line; the per-skill compactness comes from trimming each description to its first sentence (sentence boundary then the existing 200-character surrogate-safe cap), not from dropping skills.
2. **Budgets derive from the model's context window with the session model's resolution precedence** (extension-registered declaration, else the configured catalog entry, else the 128,000-token default): a soft threshold at 2% of the window informs — a one-time per-composition notice and `/context` advice to disable unused skills or packages — and a hard ceiling at 5% is a safety valve only: overflow as before (cut from the end, one names-only footer) plus a visible warning naming how many skills lost their description.
3. **`AGENTS.md` beats the ceiling.** A skill whose name appears in the workspace chain's `AGENTS.md` text is never dropped by the ceiling cut; when every eligible skill is named, the ceiling yields.
4. **`skills.indexBudget` (a positive integer or `unlimited`) overrides the computed thresholds**: a number applies one budget whose overflow follows the hard-ceiling behaviour; `unlimited` indexes everything in silence.

## Consequences

- A repository's skill pack is no longer silently truncated by a constant that assumptions outgrew; degradation is proportional to the model's window and visible when it happens.
- The `skills` spec's "Model exposure and budget" requirement carries these rules; prompt-section text changes only when the pack, the window or the budget changes (still built once per session).
- The resolved window at index-build time can differ from the session model for chat-profile-routed models — the same caveat AgentHarness compaction accepted; a wrong window degrades to the same default threshold behaviour, not to truncation by an arbitrary constant.
