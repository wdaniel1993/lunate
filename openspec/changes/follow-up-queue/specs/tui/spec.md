## MODIFIED Requirements

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

## ADDED Requirements

### Requirement: Follow-up queue

Enter SHALL queue the current input — text and attachments — while a turn runs, clear the line, and show the queue size beside the mode hint; the send key SHALL never interrupt a running turn. Queued messages SHALL run in order, one at a time, through the same submission path as a typed message: when the turn completes, when it fails, and when the user interrupts it with Esc, because the queued messages are what the user wants next. The queue SHALL hold at most five entries and SHALL refuse a sixth with a notice that keeps the existing five. Up with a non-empty queue SHALL take the whole queue back — every entry moved into the editor in order, newline-separated, ahead of any text already typed — and empty the queue; with an empty queue Up navigates history as before. A slash command SHALL be refused by the queue with a notice. The queue SHALL NOT be stored in the session, replayed on resume, or reachable from print mode.

#### Scenario: A queued message runs after the turn

- **GIVEN** a running turn
- **WHEN** the user presses Enter on `run the tests`
- **THEN** the line clears, the indicator shows one queued, and when the turn completes `run the tests` is submitted through the normal path

#### Scenario: Queued messages keep their order and never overlap

- **GIVEN** two queued messages during one turn
- **WHEN** the turn completes
- **THEN** the first runs, and the second starts only after the first completes

#### Scenario: A failed turn keeps the rest of the queue

- **GIVEN** a turn that fails with one message queued
- **WHEN** the failure is reported
- **THEN** the queued message still runs

#### Scenario: An interrupt hands over to the queue

- **GIVEN** a running turn with one message queued
- **WHEN** Esc interrupts the turn
- **THEN** the queued message runs next, in the same order it would have run had the turn completed

#### Scenario: Up takes the queue back

- **GIVEN** two queued messages and `half` typed in the line
- **WHEN** Up is pressed
- **THEN** both messages are in the editor in order, newline-separated, ahead of `half`, and the queue is empty

#### Scenario: Up still recalls history with nothing queued

- **GIVEN** an empty queue and two earlier prompts
- **WHEN** Up is pressed twice
- **THEN** the history entries are recalled in order, not a queue take-back

#### Scenario: A command cannot be queued

- **GIVEN** a running turn and `/quit` in the line
- **WHEN** Enter is pressed
- **THEN** a notice refuses the queue, the line keeps its text, and nothing is queued

#### Scenario: The sixth message is refused

- **GIVEN** five queued messages
- **WHEN** a sixth is sent
- **THEN** a notice names the five-message limit and the first five remain
