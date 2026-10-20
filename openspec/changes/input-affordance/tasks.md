# Tasks — input-affordance

The implementation landed as `bd22160` on `change/input-affordance`; this change records it in the specs. No `src/` or `tests/` file changes.

## 1. Spec delta

- [x] 1.1 Update the `tui` spec's "Input box" and "Narrow-terminal breakpoints" requirements to the shipped bands: upper rule at every width, lower rule from 60 columns, prompt marker and placeholder in the theme's `user` colour, `NO_COLOR` plain. Evidence: `bd22160`; `LiveAreaBoxRendererTests.Every_width_band_shows_the_rule_above_the_input` (40/50 → one rule, 60/80/120 → two); input goldens at 40/50/60/80/120 across truecolour/256-colour/16-colour/`NO_COLOR`.

## 2. Gate

- [x] 2.1 Run `openspec validate input-affordance --strict` and markdownlint over the new files; both clean.
