## MODIFIED Requirements

### Requirement: One meaning per key

Key events SHALL route to exactly one intent per the guide's table: Enter submits; while a turn runs it queues a follow-up, or steers the turn when the `prompt.busyEnter` setting is `steer`; Alt+Enter and Ctrl+J always edit (newline), in every state; Esc cancels a turn or returns queued follow-ups when nothing is running; Ctrl+C clears or quits, Ctrl+L opens the model picker, Ctrl+P cycles favourites, Tab completes a leading slash command, Up/Down navigate history, Alt+Up takes a queued follow-up back, everything else edits. Routing SHALL be pure, and every binding SHALL have a scripted key test.

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

- **GIVEN** an idle prompt, then a running turn under the default `prompt.busyEnter = queue`
- **WHEN** Enter is routed in each state
- **THEN** the idle route submits and the busy route queues the follow-up, and neither produces the edit or cancel intent

#### Scenario: The modifier always edits

- **GIVEN** an idle prompt, then a running turn
- **WHEN** Alt+Enter is routed in each state
- **THEN** both routes insert a newline, and neither submits, queues nor steers

## ADDED Requirements

### Requirement: Follow-up queue

Enter SHALL queue the current input — text and attachments — while a turn runs under the default `prompt.busyEnter = queue`, clear the line, and show the queue size beside the mode hint; under that default the send key never interrupts a running turn. With `prompt.busyEnter = steer`, Enter SHALL steer the running turn instead and SHALL NOT queue, and the queue stays empty because nothing can reach it. Queued messages SHALL run after the running turn completes, in order, one at a time, through the same submission path as a typed message; a failed turn SHALL NOT discard the remaining entries and a cancelled turn SHALL leave the queue intact without draining it. The queue SHALL hold at most five entries and SHALL refuse a sixth with a notice that keeps the existing five. Alt+Up SHALL move the newest queued entry into the editor when the line is empty and SHALL refuse with a notice when it is not. Esc with nothing running and a non-empty queue SHALL restore every entry to the editor in order, newline-separated, and empty the queue. A slash command SHALL be refused by the queue with a notice. The queue SHALL NOT be stored in the session, replayed on resume, or reachable from print mode.

#### Scenario: A queued message runs after the turn

- **GIVEN** a running turn and the default setting
- **WHEN** the user presses Enter on `run the tests`
- **THEN** the line clears, the indicator shows one queued, and when the turn completes `run the tests` is submitted through the normal path

#### Scenario: Steering is an explicit choice

- **GIVEN** `prompt.busyEnter = steer` and a running turn
- **WHEN** Enter is pressed on `use the other approach`
- **THEN** the text is injected into the running turn, nothing is queued, and the indicator does not appear

#### Scenario: Queued messages keep their order and never overlap

- **GIVEN** two queued messages during one turn
- **WHEN** the turn completes
- **THEN** the first runs, and the second starts only after the first completes

#### Scenario: A failed turn keeps the rest of the queue

- **GIVEN** a turn that fails with one message queued
- **WHEN** the failure is reported
- **THEN** the queued message still runs

#### Scenario: Alt+Up pulls the newest entry back

- **GIVEN** two queued messages and an empty line
- **WHEN** Alt+Up is pressed
- **THEN** the second message is in the editor, the queue holds one, and pressing again pulls the first

#### Scenario: Escape returns the whole queue

- **GIVEN** a cancelled turn and two queued messages
- **WHEN** Esc is pressed with nothing running
- **THEN** both messages are restored to the editor in order, newline-separated, and the queue is empty

#### Scenario: A command cannot be queued

- **GIVEN** a running turn and `/quit` in the line
- **WHEN** Enter is pressed
- **THEN** a notice refuses the queue, the line keeps its text, and nothing is queued

#### Scenario: The sixth message is refused

- **GIVEN** five queued messages
- **WHEN** a sixth is sent
- **THEN** a notice names the five-message limit and the first five remain
