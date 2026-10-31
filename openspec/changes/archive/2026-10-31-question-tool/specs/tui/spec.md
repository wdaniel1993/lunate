# tui Specification Delta

## ADDED Requirements

### Requirement: Question picker interaction

The interactive session SHALL present questions through the existing `SelectList` interaction, one question at a time, keeping the current selection visible. Up and Down SHALL move through options; number keys SHALL select (or toggle in multi-select mode) the corresponding option; Space SHALL toggle a multi-select option; Enter SHALL submit the current question; and Esc SHALL skip the whole question request without returning partial answers. Every question SHALL offer a free-text path, including when options are selected. A recommended option SHALL appear first and carry a visible `recommended` marker. The selection and response state SHALL survive repaint and resize.

#### Scenario: Single-select uses the existing picker keys

- **GIVEN** a single-select question with three options
- **WHEN** the user moves with arrows or presses the number for the second option and then Enter
- **THEN** the second option's label is returned for that question

#### Scenario: Multi-select returns multiple labels and free text

- **GIVEN** a multi-select question with a free-text response path
- **WHEN** the user toggles two options, enters custom text and presses Enter
- **THEN** both labels and the text are returned together

#### Scenario: Esc skips the complete request

- **GIVEN** a multi-question request with an answer already selected for an earlier question
- **WHEN** the user presses Esc on a later question
- **THEN** the request closes with `unanswered: skipped` and no partial answers

#### Scenario: Buffered and early keys answer nothing

- **GIVEN** a question prompt that has just become active
- **WHEN** a key was buffered before it appeared or arrives within the approval prompt's 300 ms arming window
- **THEN** it changes no selection or text and submits no answer

#### Scenario: Question layouts fit the Termux width band

- **GIVEN** a question with long option descriptions at 120, 80, 60 and 40 columns
- **WHEN** the question is rendered
- **THEN** the layout degrades by width, keeps controls and the selected label legible, and fits the 40-column Termux band without clipping an answer action

### Requirement: Question and answer are a paired transcript and export item

An `ask_user` interaction SHALL appear as a question-and-answer-or-outcome pair in the interactive transcript and both session export forms. An MCP elicitation SHALL appear as a labelled question/answer pair within the associated MCP tool call/result. Rendering SHALL escape user- and server-provided text as inert text.

#### Scenario: The transcript and exports preserve an answered pair

- **GIVEN** a session in which `ask_user` receives a selected label and free text
- **WHEN** the transcript, JSONL export and HTML export are viewed
- **THEN** each shows the question beside its answer as one pair in interaction order

#### Scenario: An MCP elicitation is attributed to its server

- **GIVEN** an MCP tool call that contains an elicitation from `catalog-server`
- **WHEN** the session transcript or export is viewed
- **THEN** the elicitation question and answer appear together under that tool call and name `catalog-server`
