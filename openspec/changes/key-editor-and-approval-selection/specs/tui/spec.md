## ADDED Requirements

### Requirement: External editor for the draft

`Ctrl+G` SHALL open the current draft in an external editor and put the result back in the prompt: the draft SHALL be written to a temporary file as UTF-8 without a BOM (owner-only permissions on POSIX), the live area SHALL be suspended and the terminal returned to cooked mode for the child, and the command SHALL be `$VISUAL`, else `$EDITOR`, else `vi` on POSIX and `notepad` on Windows. A variable's value SHALL be split on whitespace into a program and its arguments, with no shell and no quote interpretation. On a zero exit the live area SHALL resume, the file SHALL be read back with CRLF normalised to LF and exactly one trailing newline removed, the draft SHALL become that text and the cursor SHALL sit at its end. On a non-zero exit, a launch failure or a read-back that fails, the draft, the cursor and the stash SHALL be untouched, the live area SHALL resume, and one notice SHALL name what failed — the variable and the value it tried, the exit code, or the read that failed. A variable that is set but cannot be launched SHALL be reported, never silently replaced by the next candidate. An empty draft SHALL still open an empty buffer, and an editor that leaves the buffer empty SHALL leave the draft empty. While the child runs the session SHALL read no keys and accept no input: a key typed at the editor belongs to the editor. If the draft changed while the editor was open, the editor's text SHALL become the prompt — it is what the user just deliberately wrote — the draft that changed meanwhile SHALL be written to the stash slot, and one notice SHALL name that slot, so neither text is lost. The temporary file SHALL be deleted on every exit path, exceptions included, so no draft text is left behind in the temporary directory.

#### Scenario: A multi-line draft round-trips

- **GIVEN** a draft reading `line one` with the cursor after `line`, and an editor that leaves `line one\r\nline two\r\n`
- **WHEN** the editor exits 0
- **THEN** the draft is `line one\nline two`, the cursor is at its end, and the input box repaints at its new height

#### Scenario: An empty draft opens an empty buffer

- **GIVEN** an empty prompt
- **WHEN** `Ctrl+G` runs an editor that writes two lines
- **THEN** the editor was started on an empty file and the prompt holds those two lines afterwards

#### Scenario: The editor resolves in order

- **GIVEN** `VISUAL` and `EDITOR` both set, then only `EDITOR`, then neither
- **WHEN** `Ctrl+G` runs
- **THEN** the child is `$VISUAL`, then `$EDITOR`, then `vi` on POSIX or `notepad` on Windows, and a value carrying arguments is split into a program and its arguments with no shell in between

#### Scenario: A missing editor binary leaves the draft alone

- **GIVEN** `VISUAL` naming a program that does not exist and a draft reading `draft text`
- **WHEN** `Ctrl+G` is pressed
- **THEN** the draft and the cursor are unchanged, the live area is back, and one notice names `VISUAL` and the value it tried

#### Scenario: A non-zero exit leaves the draft alone

- **GIVEN** an editor that writes text and then exits 1
- **WHEN** `Ctrl+G` is pressed
- **THEN** the draft and the cursor are as before the command, one notice carries the exit code, and the temporary file is gone

#### Scenario: The child owns the terminal

- **GIVEN** a live frame with a running turn and an editor that writes to the terminal
- **WHEN** `Ctrl+G` runs it
- **THEN** the live area paints nothing while the child holds the terminal, the terminal is in cooked mode for the child, and the frame repaints in full on exit

#### Scenario: No key reaches the session while the editor runs

- **GIVEN** an editor that stays open for several frames
- **WHEN** keys are typed at it
- **THEN** the session reads none of them, and the draft and the history are unchanged by them

#### Scenario: The editor's text wins over a draft that changed meanwhile

- **GIVEN** a draft that changed while the editor was open
- **WHEN** the editor exits 0
- **THEN** the editor's text is in the prompt, the draft that changed meanwhile is in the stash slot, and one notice names the slot the earlier text went to

#### Scenario: The temporary file never stays behind

- **GIVEN** a successful run, a non-zero exit and a launch that throws
- **WHEN** each command returns
- **THEN** the temporary file is deleted in all three, and no draft text is left in the temporary directory

#### Scenario: The catalog decides Ctrl+G

- **GIVEN** the binding catalog
- **WHEN** `Ctrl+G` is routed in a prompt context
- **THEN** it produces the editor intent and no other row matches, a second row for the same pattern in an overlapping context is rejected by the catalog's overlap check, and an extension or keybindings file that wants `Ctrl+G` replaces that row rather than appending one

### Requirement: The guide's key table matches the catalog

`docs/guide.md`'s key table and `KeyBindingCatalog` SHALL be checked against each other by a test that fails in both directions: every key in the catalog SHALL appear in the table, and every key in the table SHALL have a catalog row. The check SHALL read only the table under the guide's `**Key bindings (one meaning each)**` heading and only its Key column, so a key named inside an Action cell is not a documented binding. A Key cell MAY list several keys separated by `/`, each spelled exactly as `KeyPatternFormatter` writes it, and a merged cell passes when every key in it is covered. Exempt from the table-to-catalog direction SHALL be exactly the panel-owned approval decisions `y`, `n` and `a`, the prompt-gated `?` whose empty-idle condition is not a catalog context, and the deliberately unbound `Ctrl+P`, each carried in the test's exemption table with a one-line reason; a new exemption is a change to this requirement. The check SHALL fail when the heading, the table or its `| Key | Action |` header is missing, so deleting the table cannot pass it, and each failure SHALL name the key, the direction and the row: `docs/guide.md documents 'Ctrl+Left' and no catalog row has it (row: 'Alt+B / Alt+F' — Move back / forward by word)` for a ghost and `KeyBindingCatalog's 'Ctrl+Enter' (Insert a newline) has no docs/guide.md row` for a gap.

#### Scenario: A catalog key the table omits fails

- **GIVEN** a catalog row whose key has no cell in the table
- **WHEN** the check runs
- **THEN** it fails naming that key and the catalog's description of it

#### Scenario: A table key with no catalog row fails

- **GIVEN** a table cell naming `Alt+R`, which no catalog row carries
- **WHEN** the check runs
- **THEN** it fails naming `Alt+R`, the direction and the row that documents it

#### Scenario: Merged cells are read key by key

- **GIVEN** a cell reading `Ctrl+A` / `Ctrl+E` and another reading `Alt+Enter` / `Shift+Enter` / `Ctrl+J` / `Ctrl+Enter`
- **WHEN** the check runs
- **THEN** every key in both cells is covered, and a key named only in an Action cell counts for nothing

#### Scenario: The exemptions are exact

- **GIVEN** `y`, `n`, `a`, `?` and `Ctrl+P` in the table with no catalog row
- **WHEN** the check runs
- **THEN** they pass with their stated reasons — the approval letters decide locally, `?` shows the approval legend and the empty-prompt help, `Ctrl+P` is deliberately unbound — and any other table key without a catalog row fails

#### Scenario: A deleted table fails instead of passing vacuously

- **GIVEN** a guide whose key heading, table or header row is gone
- **WHEN** the check runs
- **THEN** it fails naming what is missing, and does not report success

## MODIFIED Requirements

### Requirement: Approval prompt

A pending approval SHALL render as a live-area prompt naming the tool and its argument summary with `[y]es`, `[n]o`, `[a]lways this session`. The prompt SHALL be a panel: a warning headline with the concrete action (the command for `bash`, the path with its change counts for `edit` and `write`), a bounded detail block (the command's lines, the edit's diff, or the tool's key arguments, clipped never wrapped), and the question with its choices; below 60 columns the detail shortens first and every choice SHALL stay visible, never clipped mid-option. Bare `y`, `n` and `a` SHALL decide directly, wherever the selection sits. Up and Down SHALL move a selection across the three choices, wrapping from the last to the first and back, and Space SHALL activate the selected choice. The selection SHALL be prompt state carried by the prompt model, not a rendering focus: every repaint of the same prompt — a resize, a redraw, a notice arriving — SHALL show the same choice selected, and a new prompt SHALL start on `[n]o`, so Space on an untouched prompt denies, the same as Enter; approving always takes a deliberate `y`, or moving to Yes and then Space or Enter. The selection SHALL be legible without colour: a `> ` marker immediately before the selected choice's bracket — the same gutter the autocomplete uses — which grows the inline row from 34 to 36 cells and keeps every choice fully visible. A letter SHALL decide immediately whatever the selection shows; Space is the only key that acts on the selection. The choice list SHALL be the three fixed decisions, so the choice block fits any frame that can show the question: the trimming order (detail first, then headline) SHALL never reach a choice row, and a longer list is a layout change this requirement does not have. Where the three choices and the marker do not fit one row, the block SHALL stack them one per row with the gutter column kept, each choice fully visible; below the longest choice row a label SHALL clip only after its key letter, so every decision stays identifiable and selectable. The prompt SHALL arm its decisions the way the trust question does: a key read before the prompt rendered SHALL never decide, and a key read within 300 ms of it rendering SHALL decide nothing — a keystroke already in flight when the panel appeared, or a Space typed mid-word, never answers a question the user has not read. `?` SHALL render the prompt's key legend (`y` approves, `n` denies, `a` approves for the session, Space activates the selection, Enter and Escape deny) and SHALL NOT decide anything itself. Escape SHALL deny. A stray Enter SHALL deny (nothing runs by accident); all other keys SHALL neither decide nor move the selection. The prompt SHALL produce a choice; session memory for "always" is the wiring layer's concern and SHALL apply across the whole run tree — a grant given while a subagent asked covers every run of the session. When the asking run is a subagent, the prompt SHALL label it (its depth relative to the top-level run).

#### Scenario: Explicit keys decide

- **GIVEN** a pending approval
- **WHEN** `y`, `n` or `a` is pressed
- **THEN** the choice is approve, deny or always respectively

#### Scenario: Enter denies

- **GIVEN** a pending approval
- **WHEN** Enter is pressed
- **THEN** the choice is deny

#### Scenario: Selection activates with Space

- **GIVEN** a pending approval whose selection starts on `[n]o`
- **WHEN** Down is pressed twice and Space once
- **THEN** the selection has moved through `[a]lways this session` to `[y]es` and the choice is approve

#### Scenario: Space on an untouched prompt denies

- **GIVEN** a pending approval that has just rendered and whose selection is on `[n]o`
- **WHEN** Space is pressed
- **THEN** the choice is deny, the same as Enter

#### Scenario: Type-ahead never answers the prompt

- **GIVEN** a pending approval
- **WHEN** `y` was read before the prompt rendered, or `y` arrives within 300 ms of it rendering
- **THEN** nothing is decided and the prompt is still waiting

#### Scenario: The legend answers `?` without deciding

- **GIVEN** a pending approval
- **WHEN** `?` is pressed
- **THEN** the panel shows the key legend for `y`, `n`, `a`, Space, Enter and Escape, and nothing is decided

#### Scenario: Escape denies

- **GIVEN** a pending approval
- **WHEN** Escape is pressed
- **THEN** the choice is deny

#### Scenario: A subagent's request is labelled

- **GIVEN** a tool call asked by a child (subagent) run
- **WHEN** its approval prompt renders
- **THEN** the prompt names the asking run as a subagent (its depth), and pressing `a` covers that tool for the whole tree afterwards

#### Scenario: The detail is visible before deciding

- **GIVEN** a pending `bash` approval and a pending `edit` approval
- **WHEN** their panels render
- **THEN** the bash panel shows the command's lines and the edit panel shows the path, the change counts and the diff, each bounded with a marker when cut

#### Scenario: Every choice survives 40 columns

- **GIVEN** a pending approval in a 40-column terminal
- **WHEN** the panel renders
- **THEN** `[y]es`, `[n]o` and `[a]lways this session` are all fully visible on their own line and the detail is shortened, not the choices

#### Scenario: The detail never pushes the question away

- **GIVEN** a pending approval whose detail exceeds the terminal height
- **WHEN** the live frame renders
- **THEN** the detail is trimmed first and the question line always renders

#### Scenario: The selection wraps at both ends

- **GIVEN** a pending approval whose selection is on `[n]o`
- **WHEN** Up is pressed and then Down twice
- **THEN** the selection visits `[y]es`, then `[a]lways this session`, and returns to `[n]o`

#### Scenario: A letter decides over the selection

- **GIVEN** a pending approval whose selection is on `[a]lways this session`
- **WHEN** `n` is pressed
- **THEN** the choice is deny, and the selection takes no part in it

#### Scenario: The selection is visible without colour at 40 columns

- **GIVEN** a pending approval in a 40-column `NO_COLOR` frame with the selection on `[n]o`
- **WHEN** the panel renders
- **THEN** the choices read `[y]es  > [n]o  [a]lways this session`, 36 cells wide, all three choices fully visible, and the marker alone tells the selection

#### Scenario: The selection survives a repaint

- **GIVEN** a pending approval whose selection is on `[n]o`
- **WHEN** the frame is repainted, or the terminal resizes to 60 columns
- **THEN** the selection is still `[n]o`, because it is prompt state and not a rendering focus

#### Scenario: No other key moves the selection

- **GIVEN** a pending approval whose selection is on `[y]es`
- **WHEN** Left, Right, Tab, PageDown or `k` is pressed
- **THEN** no decision is made and the selection is still `[y]es`

#### Scenario: The detail leaves before a choice does

- **GIVEN** a pending approval with a long detail in a 40×12 terminal
- **WHEN** the live frame renders
- **THEN** the detail is trimmed to nothing before any choice row is dropped, and the question and all three choices still render
