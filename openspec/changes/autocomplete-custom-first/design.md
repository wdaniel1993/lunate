## Context

`CompletionRegistry.Compose` keeps the command registry's descriptor order: built-ins, then skills, then extension commands. `InteractiveSession.SetAutocomplete` truncates to `AutocompleteState.MaxRows` (8), and `AutocompleteRenderer` draws the first eight rows. With more than eight built-ins the custom rows are unreachable at a bare `/` — and the truncation is silent, so the user cannot tell they exist.

## Decision

Two changes, both in the existing seams:

1. **Ordering.** `Compose` sorts by group rank — skills and extension commands (0) before built-ins (1) — with `OrderBy`, which is stable, so descriptor order survives inside each group. No new ranking table to maintain.
2. **Budget.** `AutocompleteState.Cap` owns the rule: a list that fits is shown whole; a list that does not fits `MaxRows - 1` items plus one indicator row carrying the hidden count. The state therefore carries `HiddenCount`, and the renderer appends the indicator when it is non-zero.

## Alternatives considered

- **Rank the built-ins and insert custom entries after the "most used" few.** Needs a popularity table nobody maintains, and it makes the order depend on a guess.
- **Keep built-ins first and rely on the indicator alone.** The indicator says entries exist, but the user still has to know the filter; the card asks for discoverability, not a hint.
- **A second list or a "custom" tab.** More surface for the same information.

## Risks

Muscle memory: a bare `/` now starts with skills when any are installed. Filtering is unaffected (a query like `/new` still matches one row), and the top rows are the ones the user asked to see.
