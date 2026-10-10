## ADDED Requirements

### Requirement: Slash commands

A submitted line whose first non-space character is `/` SHALL dispatch as a client command and never reach the model: `/model` opens the model picker (`/model <id>` switches directly when the id is in the catalog), `/new` starts a fresh session, `/resume` opens the session picker, `/compact` requests an explicit out-of-run compaction, `/quit` quits. Commands SHALL append to the input history and skip the input pipeline; an unknown command SHALL produce a dim notice naming it. While a turn is running, `/new`, `/resume`, `/model` and `/compact` SHALL be refused with a notice; `/quit` SHALL quit at any time (cancelling a running turn). While an approval prompt or a picker is open, no input is submitted, so no command can dispatch.

#### Scenario: Every built-in command dispatches without a model call

- **GIVEN** an idle scripted session
- **WHEN** each built-in command is submitted
- **THEN** it takes its action, the request count is unchanged, and the command text is recalled by Up

#### Scenario: Unknown command

- **GIVEN** an idle session
- **WHEN** `/nope` is submitted
- **THEN** a dim notice names the command, no run starts, and nothing reaches the model

#### Scenario: Busy session refusals

- **GIVEN** a running turn
- **WHEN** `/new`, `/resume`, `/model` or `/compact` is submitted
- **THEN** a notice refuses it and the run is untouched

#### Scenario: /compact without a compactable tail

- **GIVEN** an idle session whose history has nothing older than the kept tail
- **WHEN** `/compact` is submitted
- **THEN** a notice says nothing was compacted

#### Scenario: Commands skip the input pipeline

- **GIVEN** an installed consuming input hook
- **WHEN** a command is submitted
- **THEN** the hook never sees the command text and the command still acts

### Requirement: SelectList pickers

A picker SHALL render as a select list in the live area — a title and one line per item (a bounded window keeps the selection visible), the selected item marked and emphasized — navigated with Up/Down (clamped at the ends), confirmed with Enter, dismissed with Esc; every other key SHALL be ignored while a picker is open. Pickers SHALL open only while idle. The model picker (Ctrl+L or `/model`) SHALL list the catalog's models, mark the current one, and on selection record a model change in the session and continue the conversation on the new model; the footer SHALL show the new model. The session picker (`/resume`) SHALL list the session directory's sessions, newest first, and on selection resume the chosen session — its history becomes the conversation and a notice names the resumed session. The picker render SHALL be pinned by a golden.

#### Scenario: Choosing a model switches without losing the conversation

- **GIVEN** an idle session with a completed turn
- **WHEN** the model picker is opened and another catalog model is chosen
- **THEN** the session gains a model-change entry, the footer names the new model, and the next request continues the same history

#### Scenario: Esc dismisses without switching

- **GIVEN** an open picker
- **WHEN** Esc is pressed
- **THEN** the picker closes and nothing changed

#### Scenario: Resume restores the conversation

- **GIVEN** two sessions in the session directory
- **WHEN** the picker resumes the older one
- **THEN** the next request carries that session's history and a notice names it

#### Scenario: Picker keys do not reach the input line

- **GIVEN** an open picker
- **WHEN** Up, Down, characters and Enter are pressed
- **THEN** only the selection changes; no text is edited and no run starts

### Requirement: Slash-command Tab completion

Tab in the input line SHALL complete a leading `/word` against the built-in command list: a single match completes to the full command; several matches complete to their longest common prefix; when no progress is possible, a dim notice SHALL list the candidates. Completion SHALL apply only when the whole input is a single `/word` with the cursor at its end; otherwise Tab SHALL do nothing. `@path` completion is deferred (T-53).

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

### Requirement: Steering echo

When the harness injects a steering message (`SteeringInjected`), the session SHALL commit a dim echo block `» <text>` to scrollback, in injection order; steering returned to the input line (Esc, error, step limit) SHALL never be echoed.

#### Scenario: Injected steering is echoed

- **GIVEN** a running turn with a queued steering message
- **WHEN** the message is injected before the next model call
- **THEN** the scrollback gains the dim echo, in order

#### Scenario: Returned leftovers are not echoed

- **GIVEN** queued steering that the user takes back with Esc
- **WHEN** the run ends
- **THEN** no echo for it appears in scrollback

### Requirement: Interactive entry

Bare `lunate` (no arguments) SHALL start interactive mode when the console is a terminal; when it is not (or `TERM=dumb`), it SHALL exit non-zero with a hint to use `lunate -p`. Interactive mode SHALL read keys through the real console and schedule on the default scheduler; `--help` SHALL document the entry and list the commands.

#### Scenario: Not a terminal

- **GIVEN** a console that is not interactive
- **WHEN** `lunate` runs
- **THEN** it exits with a non-zero code and the hint names `lunate -p`

#### Scenario: A terminal starts the session

- **GIVEN** a scripted terminal (test seam)
- **WHEN** `lunate` runs
- **THEN** the interactive session starts and ends with the console

## MODIFIED Requirements

### Requirement: One meaning per key

Key events SHALL route to exactly one intent per the guide's table: Enter submits, Alt+Enter and Ctrl+J edit (newline), Esc cancels, Ctrl+C clears or quits, Ctrl+L opens the model picker, Tab completes a leading slash command, Up/Down navigate history, everything else edits. Routing SHALL be pure, and every binding SHALL have a scripted key test.

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
