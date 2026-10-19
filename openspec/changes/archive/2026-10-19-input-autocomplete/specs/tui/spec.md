# tui — input box and autocomplete deltas

## REMOVED Requirements

### Requirement: Slash-command Tab completion

Tab in the input line SHALL complete a leading `/word` against the built-in command list: a single match completes to the full command; several matches complete to their longest common prefix; when no progress is possible, a dim notice SHALL list the candidates. Slash completion SHALL apply only when the whole input is a single `/word` with the cursor at its end; when a path token applies (see the path completion requirement) it takes precedence, and otherwise Tab SHALL do nothing.

#### Scenario: A single match completes

- **GIVEN** the input `/comp`
- **WHEN** Tab is pressed
- **THEN** the input becomes `/compact`

#### Scenario: Several matches complete to the common prefix

- **GIVEN** a candidate list whose matches share a longer prefix than the typed word
- **WHEN** Tab is pressed
- **THEN** the input becomes that longest common prefix

#### Scenario: No progress lists the candidates

- **GIVEN** the input `/` (every command matches, no longer prefix exists)
- **WHEN** Tab is pressed
- **THEN** a dim notice lists the commands and the input is unchanged

#### Scenario: Tab outside a slash word does nothing

- **GIVEN** an input that is empty, mid-word, or contains whitespace
- **WHEN** Tab is pressed
- **THEN** nothing changes

### Requirement: Path completion and file index

Tab in the input line SHALL complete an `@path` token: when the cursor sits at the end of a non-whitespace token that starts with `@`, the text after the `@` SHALL be completed against a workspace file index — a single match replaces the token (keeping the `@`); several matches replace it with their longest common prefix; when no progress is possible, a dim notice SHALL list up to 20 candidates with an ellipsis count beyond that. When the index is not ready yet, the session SHALL show an indexing notice and start the build. The index SHALL be built lazily (never at startup), asynchronously, honouring `.gitignore` files (root and nested; negation, directory-only and anchoring rules) while always skipping `.git`; directory symlinks SHALL not be followed. It SHALL be bounded to 200,000 entries (sorted ordinals; a truncation flag is exposed) and a completion query SHALL be ordinal. On a generated 20,000-file repository the build SHALL stay within 5 seconds and a warm query within 50 milliseconds, proven by a budget check in the gate.

#### Scenario: A single match completes the token

- **GIVEN** the input `read @src/co` with the cursor at its end and a unique match `src/core/` (a directory)
- **WHEN** Tab is pressed
- **THEN** the input becomes `read @src/core/`

#### Scenario: Several matches complete to the common prefix, none lists candidates

- **GIVEN** `@src/c` matching `src/cli/` and `src/core/`, or a prefix with no matches
- **WHEN** Tab is pressed
- **THEN** the input becomes `@src/c` extended to the longest common prefix, or a dim notice lists up to 20 candidates (`… (+N more)` beyond) and the input is unchanged

#### Scenario: Cursor not at a token end falls through

- **GIVEN** an input whose cursor is not at the end of an `@token`
- **WHEN** Tab is pressed
- **THEN** the slash rules apply and, outside their conditions, nothing changes

#### Scenario: Ignores are honoured

- **GIVEN** a workspace whose `.gitignore` excludes `bin/` and whose tree contains `bin/app.dll`
- **WHEN** the index is queried for `bin`
- **THEN** no ignored entry is returned and `.git` never appears

#### Scenario: Lazy and bounded

- **GIVEN** a started TUI with no path completion attempted
- **WHEN** the session runs
- **THEN** no index build has started (the startup budget is unaffected); the first `@` completion attempt starts the build and shows the indexing notice, and an over-cap workspace exposes the truncation flag with a deterministic entry set

#### Scenario: The large-repository budget holds

- **GIVEN** the generated 20,000-file budget tree in the gate
- **WHEN** the budget step runs
- **THEN** the build completes within 5 seconds and a warm query within 50 milliseconds

## ADDED Requirements

### Requirement: Input box

The live area SHALL render the input as a box: a full-width rule above and below the input at widths of 80 columns or more (none below), the input's first line carrying the `> ` prompt prefix and continuation lines a two-space indent, and the status footer below the box. When the input is empty, the first line SHALL show the placeholder `Ask anything, / for commands` in the muted style. The box SHALL grow with its content to eight lines and scroll beyond that, keeping the cursor's line visible; the rules and placeholder SHALL use ASCII fallbacks (`-`) when Unicode is unavailable, and the rules SHALL be omitted below 80 columns while the input and footer stay.

#### Scenario: The box draws rules at 80 or more columns

- **GIVEN** a terminal 120 columns wide
- **WHEN** the input renders
- **THEN** a rule spans the width above and below the input and the footer renders below the lower rule

#### Scenario: No rules below 80 columns

- **GIVEN** a terminal 50 columns wide
- **WHEN** the input renders
- **THEN** no rules are drawn, the `> ` input line and the compact footer render

#### Scenario: The placeholder shows when empty

- **GIVEN** an empty input
- **WHEN** the box renders
- **THEN** the first line shows `Ask anything, / for commands` in the muted style

#### Scenario: Growth to eight lines then scroll

- **GIVEN** an input of more than eight wrapped lines
- **WHEN** the box renders
- **THEN** exactly the eight lines around the cursor render and the rest scroll

#### Scenario: ASCII fallback

- **GIVEN** a terminal without Unicode support at 80 or more columns
- **WHEN** the box renders
- **THEN** the rules use `-` runs and no box character leaks

### Requirement: Autocomplete

Typing SHALL open an as-you-type autocomplete list above the input: when the input's first word starts with `/`, the list SHALL show the built-in commands, the `/skill:<name>` entries and the extension commands, each with a short description where one exists; when the cursor sits at the end of an `@` token, the list SHALL show the workspace files matching that token from the file index. The list SHALL be one registry composed from the command table, the skill catalog and the extension manifests — no separate hard-coded lists — filtered fuzzily as a subsequence of the typed query for the fixed registry, while file rows come from the index's ordinal query. At most eight rows SHALL render, directly above the input; ↑/↓ SHALL move the selection, Tab and Enter SHALL accept it (replacing the token, keeping the `@` for file rows and closing the list), Esc SHALL close the list, and typing SHALL re-filter. Descriptions SHALL be dropped below 60 columns; file rows SHALL render the directory part muted; with no matches the list SHALL stay closed.

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

The workspace file index SHALL be built lazily (never at startup), asynchronously, honouring `.gitignore` files (root and nested; negation, directory-only and anchoring rules) while always skipping `.git`; directory symlinks SHALL not be followed. It SHALL be bounded to 200,000 entries (sorted ordinals; a truncation flag is exposed) and a completion query SHALL be ordinal. When the index is not ready yet, the session SHALL show an indexing notice and start the build. On a generated 20,000-file repository the build SHALL stay within 5 seconds and a warm query within 50 milliseconds, proven by a budget check in the gate.

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
