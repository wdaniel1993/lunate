## ADDED Requirements

### Requirement: One key, one meaning

Key events SHALL route to exactly one intent through a single binding catalog: an ordered list of rows carrying a key pattern, an intent, the contexts in which the row applies and a description. Routing SHALL be pure and SHALL consider only the catalog and the active contexts; a key with no applicable row SHALL edit. The prompt SHALL bind: Enter submits, or queues a follow-up when a turn is running; Alt+Enter, Shift+Enter (where the terminal reports it) and Ctrl+J always insert a newline, in every state; Esc interrupts a running turn; Up with a non-empty queue takes the whole queue back into the editor, and otherwise Up/Down navigate history; Ctrl+C interrupts a running turn, clears an idle prompt that holds text, and arms the quit window only when the prompt is empty and idle; Ctrl+D arms the quit window only when the prompt is empty and idle; Ctrl+L forces a full redraw preserving the input and the conversation; Alt+P opens the model picker; Ctrl+R opens history search; Ctrl+G opens the external editor; Ctrl+S stashes or restores the draft; Ctrl+_ undoes the last input edit; Ctrl+O opens the transcript viewer; Tab completes a leading slash command; the line editor binds Ctrl+A and Ctrl+E to line start and end, Ctrl+K and Ctrl+U to kill to end and to start, Ctrl+W to kill the word before the cursor, Alt+D to kill the word after it, Ctrl+Y to yank the last kill, and Alt+B and Alt+F to move by word. Ctrl+P SHALL be unbound. No two rows SHALL overlap within a context, every row SHALL carry a non-empty description, and every binding SHALL have a scripted key test.

#### Scenario: Every binding routes distinctly

- **GIVEN** the catalog's rows
- **WHEN** each row's key is routed in one of its contexts
- **THEN** it produces that row's intent and no other row's, and the catalog has no overlap within a context

#### Scenario: Every row is described

- **GIVEN** the catalog
- **WHEN** it is inspected
- **THEN** every row has a description that names what the key does, and the guide's table carries the same rows

#### Scenario: Ctrl+C interrupts a running turn

- **GIVEN** a running turn
- **WHEN** Ctrl+C is routed
- **THEN** it produces the interrupt intent, the turn is cancelled, and any queued follow-ups remain queued

#### Scenario: Ctrl+C clears or arms when idle

- **GIVEN** an idle prompt with text, then an idle empty prompt
- **WHEN** Ctrl+C is routed in each state
- **THEN** the first clears the input and the second arms the quit window

#### Scenario: Ctrl+L redraws instead of opening a picker

- **GIVEN** a prompt holding text
- **WHEN** Ctrl+L is routed
- **THEN** it produces the redraw intent, the buffer is unchanged, and no picker opens

#### Scenario: Tab completes instead of editing

- **GIVEN** an input that starts with `/`
- **WHEN** Tab is routed
- **THEN** it produces the complete intent, not edit

#### Scenario: Ctrl+P is unbound

- **GIVEN** the prompt is idle
- **WHEN** Ctrl+P is routed
- **THEN** it produces the edit intent and no session action

#### Scenario: The send key sends in both states

- **GIVEN** an idle prompt, then a running turn
- **WHEN** Enter is routed in each state
- **THEN** the idle route submits and the busy route queues the follow-up, and neither produces the edit or interrupt intent

#### Scenario: Every newline key edits in both states

- **GIVEN** an idle prompt, then a running turn
- **WHEN** Alt+Enter, Shift+Enter or Ctrl+J is routed in each state
- **THEN** all six routes insert a newline, and none submits, queues or interrupts

## MODIFIED Requirements

### Requirement: Ctrl+C quit window

On an empty input with no turn running, Ctrl+C SHALL arm a quit hint, and a second press within two seconds SHALL quit; with non-empty input it SHALL clear the input and disarm. While a turn runs, Ctrl+C SHALL interrupt the turn and SHALL NOT arm the window. Ctrl+D SHALL quit on a second press within 800 milliseconds, armed only when the input is empty and no turn runs; with text present or a turn running it SHALL NOT arm. Both windows SHALL run on the injected scheduler — virtual time in tests, no wall clock.

#### Scenario: Second press within the window quits

- **GIVEN** an armed window after one Ctrl+C on an empty input
- **WHEN** a second Ctrl+C arrives within two seconds of virtual time
- **THEN** the action is quit

#### Scenario: Expired window re-arms

- **GIVEN** an armed window
- **WHEN** more than two seconds of virtual time pass before the next Ctrl+C
- **THEN** that press re-arms instead of quitting

#### Scenario: Ctrl+C interrupts instead of arming

- **GIVEN** a running turn and an empty input
- **WHEN** Ctrl+C is pressed twice within two seconds
- **THEN** the turn is cancelled once and the session does not quit

#### Scenario: Ctrl+D quits on the second press

- **GIVEN** an idle, empty prompt
- **WHEN** Ctrl+D is pressed twice within 800 milliseconds of virtual time
- **THEN** the action is quit

#### Scenario: Ctrl+D does not arm mid-turn or with text

- **GIVEN** a running turn, and separately an idle prompt holding text
- **WHEN** Ctrl+D is pressed twice in either state
- **THEN** no quit occurs and the input is unchanged

### Requirement: Approval prompt

A pending approval SHALL render as a live-area prompt naming the tool and its argument summary with `[y]es`, `[n]o`, `[a]lways this session`. The prompt SHALL be a panel: a warning headline with the concrete action (the command for `bash`, the path with its change counts for `edit` and `write`), a bounded detail block (the command's lines, the edit's diff, or the tool's key arguments, clipped never wrapped), and the question with its choices; below 60 columns the detail shortens first and every choice SHALL stay visible, never clipped mid-option. Bare `y`, `n` and `a` SHALL decide directly. Up and Down SHALL move a selection across the three choices, rendering it without clipping any choice, and Space SHALL activate the selected choice. Escape SHALL deny. A stray Enter SHALL deny (nothing runs by accident); all other keys SHALL not decide. The prompt SHALL produce a choice; session memory for "always" is the wiring layer's concern and SHALL apply across the whole run tree — a grant given while a subagent asked covers every run of the session. When the asking run is a subagent, the prompt SHALL label it (its depth relative to the top-level run).

#### Scenario: Explicit keys decide

- **GIVEN** a pending approval
- **WHEN** `y`, `n` or `a` is pressed
- **THEN** the choice is approve, deny or always respectively

#### Scenario: Enter denies

- **GIVEN** a pending approval
- **WHEN** Enter is pressed
- **THEN** the choice is deny

#### Scenario: Selection activates with Space

- **GIVEN** a pending approval
- **WHEN** Down is pressed twice and Space once
- **THEN** the selection has moved to `[a]lways this session` and the choice is always

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

### Requirement: Input line editing

The input line SHALL support editing through events: insert (typed characters and pasted text), backspace, delete, cursor movement by character, word movement with Alt+B and Alt+F, line start and end with Ctrl+A and Ctrl+E, home/end, and newline insertion. Ctrl+K and Ctrl+U SHALL kill from the cursor to the end and to the start of the line, Ctrl+W and Alt+D SHALL kill the word before and after the cursor, Ctrl+Y SHALL insert the most recent kill, and Ctrl+_ SHALL undo the last edit, restoring the exact buffer and cursor. Undo history SHALL be bounded at 64 entries and cleared by submit and by newline insertion. Cursor and paint math SHALL use cell widths (combining marks zero, wide characters two) so non-ASCII text never misplaces the cursor; anything beyond the compact width table is a documented follow-up. Word boundaries SHALL be computed on cell text, never splitting a surrogate pair.

#### Scenario: Cursor math with wide characters

- **GIVEN** an input line containing CJK characters
- **WHEN** the cursor moves left or the line renders
- **THEN** columns are computed by cell width, not character count

#### Scenario: Paste inserts literally

- **GIVEN** a paste event containing newlines
- **WHEN** it is applied to the input line
- **THEN** the buffer contains the text unchanged (normalized newlines) and the cursor is after it

#### Scenario: Kill and yank round-trip

- **GIVEN** the input `git commit -m "x"` with the cursor after `git`
- **WHEN** Ctrl+K is pressed and then Ctrl+Y
- **THEN** the buffer is unchanged from before the kill and the cursor is at the end

#### Scenario: Undo restores buffer and cursor

- **GIVEN** an edit that inserted and then deleted text
- **WHEN** Ctrl+_ is pressed
- **THEN** the buffer and the cursor are exactly as before the last edit

#### Scenario: Word movement respects wide characters

- **GIVEN** a line mixing CJK and Latin words
- **WHEN** Alt+F moves forward
- **THEN** the cursor lands on the next word start without splitting a surrogate pair

## REMOVED Requirements

### Requirement: One meaning per key

Key events SHALL route to exactly one intent per the guide's table: Enter submits, or queues a follow-up when a turn is running; Alt+Enter, Shift+Enter (where the terminal reports it) and Ctrl+J always edit (newline), in every state; Esc interrupts a running turn; Up with a non-empty queue takes the whole queue back into the editor, and otherwise Up/Down navigate history; Ctrl+C clears or quits, Ctrl+L opens the model picker, Ctrl+P cycles favourites, Tab completes a leading slash command, everything else edits. Routing SHALL be pure, and every binding SHALL have a scripted key test.

#### Scenario: Every binding routes distinctly

- **GIVEN** the key events of the guide's binding table
- **WHEN** each is routed
- **THEN** it produces its own intent (and no other binding's), proven by the scripted matrix

#### Scenario: Ctrl+C never cancels a turn

- **GIVEN** a running turn
- **WHEN** Ctrl+C is pressed (any number of times)
- **THEN** the input clears or the quit window arms/quits; no cancel intent exists for it

#### Scenario: Tab completes instead of editing

- **GIVEN** an input that starts with `/`
- **WHEN** Tab is routed
- **THEN** it produces the complete intent, not edit

#### Scenario: Ctrl+P is its own intent

- **GIVEN** the prompt is idle
- **WHEN** Ctrl+P is routed
- **THEN** it produces the favourites-cycle intent and not the model-picker or edit intent

#### Scenario: The send key sends in both states

- **GIVEN** an idle prompt, then a running turn
- **WHEN** Enter is routed in each state
- **THEN** the idle route submits and the busy route queues the follow-up, and neither produces the edit or cancel intent

#### Scenario: Every newline key edits in both states

- **GIVEN** an idle prompt, then a running turn
- **WHEN** Alt+Enter, Shift+Enter or Ctrl+J is routed in each state
- **THEN** all six routes insert a newline, and none submits, queues or cancels
