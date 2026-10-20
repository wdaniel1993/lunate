# tui — approval-panel deltas

## MODIFIED Requirements

### Requirement: Approval prompt

A pending approval SHALL render as a live-area prompt naming the tool and its argument summary with `[y]es`, `[n]o`, `[a]lways this session`. The prompt SHALL be a panel: a warning headline with the concrete action (the command for `bash`, the path with its change counts for `edit` and `write`), a bounded detail block (the command's lines, the edit's diff, or the tool's key arguments, clipped never wrapped), and the question with its choices; below 60 columns the detail shortens first and every choice SHALL stay visible, never clipped mid-option. Only bare `y`, `n` and `a` SHALL decide; a stray Enter SHALL deny (nothing runs by accident); all other keys SHALL not decide. The prompt SHALL produce a choice; session memory for "always" is the wiring layer's concern and SHALL apply across the whole run tree — a grant given while a subagent asked covers every run of the session. When the asking run is a subagent, the prompt SHALL label it (its depth relative to the top-level run).

#### Scenario: Explicit keys decide

- **GIVEN** a pending approval
- **WHEN** `y`, `n` or `a` is pressed
- **THEN** the choice is approve, deny or always respectively

#### Scenario: Enter denies

- **GIVEN** a pending approval
- **WHEN** Enter is pressed
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
