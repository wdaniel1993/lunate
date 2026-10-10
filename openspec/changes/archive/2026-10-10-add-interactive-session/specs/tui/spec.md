## ADDED Requirements

### Requirement: Input pipeline

Every submitted text — a new turn or a steering message — SHALL pass through the `InputReceived` hook chain before anything starts or queues: a transformed text is what proceeds, a consumed input starts and queues nothing and is reported as a notice. With no hook runner configured, input passes through unchanged.

#### Scenario: A consuming handler stops the input

- **GIVEN** an installed handler that consumes input
- **WHEN** the user submits text
- **THEN** no run starts and nothing is queued, and a notice is shown

### Requirement: Interactive session wiring

The interactive session SHALL consume the harness's event stream: streaming text renders as a live tail whose completed paragraphs commit to scrollback as Markdown; tool results commit as tool blocks; usage updates the footer; retrying, compaction and step-limit appear as notices. The approval prompt SHALL appear in the live area and block new input until decided; `always` memory SHALL be session-scoped per tool name. Input history SHALL live in `~/.lunate/history` and be navigable with Up/Down.

#### Scenario: Streaming commits completed paragraphs

- **GIVEN** a run whose text output spans several paragraphs
- **WHEN** paragraphs complete during streaming
- **THEN** completed paragraphs are rendered as Markdown in scrollback and only the unfinished tail stays live

#### Scenario: Approval blocks steering

- **GIVEN** an open approval prompt
- **WHEN** the user types
- **THEN** the input line accepts no new text; only prompt keys and the quit window act

### Requirement: Steering interaction semantics

`Esc` SHALL cancel the running turn and return any unsent queued steering to the input line. A normally finished run with leftover steering SHALL auto-start the next run with that text; a run that ended in error or step limit SHALL not auto-run and SHALL return the leftover to the input line.

#### Scenario: Esc returns queued steering to the input

- **GIVEN** a running turn with a queued steering message
- **WHEN** the user presses Esc
- **THEN** the turn cancels and the queued text appears in the input line, unsent

#### Scenario: Normal finish auto-runs leftover steering

- **GIVEN** a run that finishes normally with leftover queued steering
- **WHEN** the run ends
- **THEN** the next run starts automatically with the leftover text

#### Scenario: Error does not auto-run

- **GIVEN** a run that ends in an error or step limit with leftover queued steering
- **WHEN** the run ends
- **THEN** no run starts and the leftover text returns to the input line
