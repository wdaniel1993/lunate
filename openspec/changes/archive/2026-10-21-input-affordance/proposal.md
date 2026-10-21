# Proposal — input-affordance

## Why

Commit `bd22160` changed the input box, but the `tui` spec still states the old rule ("below 80 columns the box rules are omitted"), so spec and code disagree — and the spec is the source of truth. This change records the shipped behaviour before the branch is archived.

## What Changes

- The input box draws its upper rule at every width; the lower rule is drawn from 60 columns.
- The `>` prompt marker and, while the input is empty, the placeholder `Ask anything, / for commands` use the theme's `user` colour (`#87afd7`, xterm 110; plain under `NO_COLOR`).
- The compact band therefore keeps the upper rule and loses only the lower one.
- No code change: the implementation and its goldens are already in `bd22160`.

## Capabilities

### New Capabilities

- None.

### Modified Capabilities

- `tui`: the "Input box" and "Narrow-terminal breakpoints" requirements change their rule boundaries and the prompt tone; every other requirement is untouched.

## Impact

- `openspec/changes/input-affordance/` only: the `tui` delta spec.
- No `src/` or `tests/` change, no public API change, no new package, no ADR.
- `README.md` and `docs/guide.md` already state the new behaviour in `bd22160`.
