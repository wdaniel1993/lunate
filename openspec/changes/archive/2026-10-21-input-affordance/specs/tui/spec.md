<!-- markdownlint-disable MD038 — the prompt string "> " carries its trailing space deliberately. -->

# tui — input-affordance deltas

## MODIFIED Requirements

### Requirement: Input box

The live area SHALL render the input as a box: a full-width rule above the input at every width, a second full-width rule below it from 60 columns (none below), the input's first line carrying the `> ` prompt prefix and continuation lines a two-space indent, and the status footer below the box — below the lower rule when it is drawn, directly below the input when it is not. When the input is empty, the first line SHALL show the placeholder `Ask anything, / for commands` in the theme's `user` colour; the `> ` prompt marker SHALL carry that colour whenever the input renders (plain under `NO_COLOR`), while typed text and continuation lines stay plain. The box SHALL grow with its content to eight lines and scroll beyond that, keeping the cursor's line visible; the rules and placeholder SHALL use ASCII fallbacks (`-`) when Unicode is unavailable, and the lower rule SHALL be omitted below 60 columns while the upper rule, the input and footer stay.

#### Scenario: The box draws rules at 80 or more columns

- **GIVEN** a terminal 120 columns wide
- **WHEN** the input renders
- **THEN** a rule spans the width above and below the input and the footer renders below the lower rule

#### Scenario: No rules below 80 columns

- **GIVEN** a terminal 50 columns wide
- **WHEN** the input renders
- **THEN** the upper rule spans the width, the lower rule is absent, and the `> ` input line and the compact footer render

#### Scenario: The placeholder shows when empty

- **GIVEN** an empty input
- **WHEN** the box renders
- **THEN** the first line shows the prompt marker and `Ask anything, / for commands` in the theme's `user` colour (plain under `NO_COLOR`)

#### Scenario: Growth to eight lines then scroll

- **GIVEN** an input of more than eight wrapped lines
- **WHEN** the box renders
- **THEN** exactly the eight lines around the cursor render and the rest scroll

#### Scenario: ASCII fallback

- **GIVEN** a terminal without Unicode support at 80 or more columns
- **WHEN** the box renders
- **THEN** the rules use `-` runs and no box character leaks

### Requirement: Narrow-terminal breakpoints

The TUI SHALL evaluate three width bands at render time: `≥100` full, `60–99` standard and `<60` compact. In the compact band the tool headers shorten per the tool-block requirement, the footer takes its compact form per the status-footer requirement, the input box keeps its upper rule and loses the lower one (its ≥60 boundary already covers this), and autocomplete descriptions are absent (their ≥60 boundary already covers this). Every rendered line SHALL be no wider than the terminal. A resize SHALL re-render the live area at the new width before any further output, and scrollback committed after the resize SHALL use the new width; already-committed scrollback is immutable and owned by the terminal. When the diff gutter and markdown tables enter the renderers, the compact band SHALL render diffs without the gutter and tables as lists; until then those constructs keep their plain-text forms.

#### Scenario: The full band keeps the full layout

- **GIVEN** a 120-column terminal
- **WHEN** a frame renders
- **THEN** footers carry every segment, tool headers carry their summaries and the box rules are present

#### Scenario: The compact band applies its rules

- **GIVEN** a 40-column terminal
- **WHEN** a frame with a committed tool block and the input renders
- **THEN** the header is short, the footer reads `model · <pct>%`, the upper rule is drawn, the lower rule is absent and every line fits the width

#### Scenario: Nothing renders wider than the terminal

- **GIVEN** any frame at 40×20, 60×30, 80×24 or 120×40
- **WHEN** its lines are measured in cells
- **THEN** no line exceeds the terminal width

#### Scenario: A resize re-renders at the new width

- **GIVEN** a running session at 40 columns
- **WHEN** the terminal resizes to 120 columns
- **THEN** the next frame renders the full-band layout, and resizing back to 60 renders the compact/standard rules for that width
