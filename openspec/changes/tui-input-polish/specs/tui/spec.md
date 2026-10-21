# TUI delta — input polish

## MODIFIED Requirements

### Requirement: Input box

The live area SHALL render the input as a box: a full-width rule above the input at every width, a second full-width rule below it at every width, the input's first line carrying the `> ` prompt prefix and continuation lines a two-space indent, and the status footer below the box — below the lower rule when it is drawn, directly below the input when it is not. When the input is empty, the first line SHALL show the placeholder `Ask anything, / for commands` in the theme's `user` colour; the `> ` prompt marker SHALL carry that colour whenever the input renders (plain under `NO_COLOR`), while typed text and continuation lines stay plain. The box SHALL grow with its content to eight lines and scroll beyond that, keeping the cursor's line visible; the rules and placeholder SHALL use ASCII fallbacks (`-`) when Unicode is unavailable, and the lower rule SHALL be drawn at every width so the box always closes — the compact band keeps its other reductions, not this one — while the upper rule, the input and footer stay.

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

### Requirement: Autocomplete

Typing SHALL open an as-you-type autocomplete list above the input: when the input's first word starts with `/`, the list SHALL show the built-in commands, the `/skill:<name>` entries and the extension commands, each with a short description where one exists; when the cursor sits at the end of an `@` token, the list SHALL show the workspace files matching that token from the file index. The list SHALL be one registry composed from the command table, the skill catalog and the extension manifests — no separate hard-coded lists — filtered fuzzily as a subsequence of the typed query for the fixed registry, while file rows come from the index's case-insensitive completion query. At most eight rows SHALL render, directly above the input; ↑/↓ SHALL move the selection, Tab and Enter SHALL accept it (replacing the token, keeping the `@` for file rows and closing the list), Esc SHALL close the list, and typing SHALL re-filter. Descriptions SHALL be dropped below 60 columns; file rows SHALL render the directory part muted; with no matches the list SHALL stay closed.

#### Scenario: The list opens on slash

- **GIVEN** an empty input
- **WHEN** `/` is typed
- **THEN** the list shows the commands, skills and extension commands with descriptions, capped at eight rows above the input

#### Scenario: Typing filters fuzzily

- **GIVEN** the open list
- **WHEN** `/thk` is typed
- **THEN** the list shows the subsequence matches (for example `/thinking`) and closes when nothing matches

#### Scenario: Arrows and accept

- **GIVEN** the open list with several rows
- **WHEN** ↓ moves to the second row and Tab is pressed
- **THEN** the input becomes that command and the list closes

#### Scenario: Enter accepts instead of submitting

- **GIVEN** the open list
- **WHEN** Enter is pressed
- **THEN** the selected item is accepted and no turn is submitted

#### Scenario: Esc closes the list first

- **GIVEN** the open list
- **WHEN** Esc is pressed
- **THEN** the list closes and the input keeps its text

#### Scenario: File mode lists index matches

- **GIVEN** the input `read @src/co` with the cursor at its end and the index ready
- **WHEN** the list renders
- **THEN** the matching paths appear with the directory part muted and accepting keeps the `@`

#### Scenario: The registry is one source

- **GIVEN** a session with discovered skills and loaded extensions
- **WHEN** the slash list opens
- **THEN** the `/skill:<name>` entries and the extension command names appear alongside the built-ins, each described

#### Scenario: Descriptions drop below 60 columns

- **GIVEN** a terminal 50 columns wide
- **WHEN** the list renders
- **THEN** rows show names only

### Requirement: File index

The workspace file index SHALL be built lazily (never at startup), asynchronously, honouring `.gitignore` files (root and nested; negation, directory-only and anchoring rules) while always skipping `.git`; directory symlinks SHALL not be followed. It SHALL be bounded to 200,000 entries (sorted ordinals; a truncation flag is exposed) and a completion query SHALL be case-insensitive while the index stays sorted ordinally. When the index is not ready yet, the session SHALL show an indexing notice and start the build. On a generated 20,000-file repository the build SHALL stay within 5 seconds and a warm query within 50 milliseconds, proven by a budget check in the gate.

#### Scenario: Ignores are honoured

- **GIVEN** a workspace whose `.gitignore` excludes `bin/` and whose tree contains `bin/app.dll`
- **WHEN** the index is queried for `bin`
- **THEN** no ignored entry is returned and `.git` never appears

#### Scenario: Lazy and bounded

- **GIVEN** a started TUI with no file completion attempted
- **WHEN** the session runs
- **THEN** no index build has started (the startup budget is unaffected); the first `@` completion attempt starts the build and shows the indexing notice, and an over-cap workspace exposes the truncation flag with a deterministic entry set

#### Scenario: The large-repository budget holds

- **GIVEN** the generated 20,000-file budget tree in the gate
- **WHEN** the budget step runs
- **THEN** the build completes within 5 seconds and a warm query within 50 milliseconds

### Requirement: Narrow-terminal breakpoints

The TUI SHALL evaluate three width bands at render time: `≥100` full, `60–99` standard and `<60` compact. In the compact band the tool headers shorten per the tool-block requirement, the footer takes its compact form per the status-footer requirement, the input box keeps its upper and lower rules, and autocomplete descriptions are absent (their ≥60 boundary already covers this). Every rendered line SHALL be no wider than the terminal. A resize SHALL re-render the live area at the new width before any further output, and scrollback committed after the resize SHALL use the new width; already-committed scrollback is immutable and owned by the terminal. When the diff gutter and markdown tables enter the renderers, the compact band SHALL render diffs without the gutter and tables as lists; until then those constructs keep their plain-text forms.

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

## ADDED Requirements

### Requirement: Notice rendering

A live-area notice SHALL wrap to the terminal width instead of being clipped, so a long message loses no content. Each physical line SHALL be sanitized and SHALL be no wider than the terminal; the whole notice SHALL be capped at four lines and SHALL end its last line with an ellipsis marker when it overflows. The wrapped notice's real line count SHALL feed the live area's budget, so the input box keeps its eight lines and the tail loses the rows instead. A notice that sanitizes to nothing SHALL render as none, and a capped notice SHALL never hide the input.

#### Scenario: A long notice wraps

- **GIVEN** a 40-column terminal
- **WHEN** a notice longer than the width is rendered
- **THEN** it wraps at the width, no word is lost, and every physical line is at most 40 columns

#### Scenario: A pathological notice is capped

- **GIVEN** an 80-column terminal
- **WHEN** a notice longer than four lines is rendered
- **THEN** it occupies four lines, the last ends with the ellipsis marker, and the input box is still fully rendered
