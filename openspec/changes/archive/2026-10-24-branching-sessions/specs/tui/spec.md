## Purpose

The TUI gains the three commands that expose the session tree, with the refusal rules that keep a branch point meaningful.

## ADDED Requirements

### Requirement: Tree, fork and clone commands

`/tree` SHALL render the session's tree from the transcript model, pickable by `j`/`k` and `/` filter, with `Enter` putting the picked user message back in the editor and never resending it. `/fork <entry>` SHALL create a new session file at that point; `/clone` SHALL copy the whole session. `/rewind <entry>` SHALL move the leaf back without a confirmation step, and its notice SHALL state that the previous branch is still reachable in `/tree`; `/rewind <entry> summary` SHALL ask for confirmation first, naming the model that will be called and an estimate of the call's cost. `/fork`, `/clone` and an in-place rewind SHALL be refused while a run is active, with a notice saying the turn is still running, and SHALL create and write nothing before that check. `/tree` SHALL be available while a run is active because it only reads.

#### Scenario: Enter restores without sending

- **GIVEN** `/tree` open with a picked user message
- **WHEN** `Enter` is pressed
- **THEN** the message is in the editor and no request is made

#### Scenario: A run blocks the commands that write

- **GIVEN** an active run
- **WHEN** `/fork`, `/clone` or a rewind is requested
- **THEN** a notice says the turn is still running and no file is created or rewritten
