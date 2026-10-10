## MODIFIED Requirements

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

## ADDED Requirements

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
