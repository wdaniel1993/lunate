## ADDED Requirements

### Requirement: One meaning per key

Key events SHALL route to exactly one intent per the guide's table: Enter submits, Alt+Enter and Ctrl+J edit (newline), Esc cancels, Ctrl+C clears or quits, Ctrl+L opens the model picker, Up/Down navigate history, everything else edits. Routing SHALL be pure, and every binding SHALL have a scripted key test.

#### Scenario: Every binding routes distinctly

- **GIVEN** the key events of the guide's binding table
- **WHEN** each is routed
- **THEN** it produces its own intent (and no other binding's), proven by the scripted matrix

#### Scenario: Ctrl+C never cancels a turn

- **GIVEN** a running turn
- **WHEN** Ctrl+C is pressed (any number of times)
- **THEN** the input clears or the quit window arms/quits; no cancel intent exists for it

### Requirement: Ctrl+C quit window

On an empty input, Ctrl+C SHALL arm a quit hint, and a second press within two seconds SHALL quit; with non-empty input it SHALL clear the input and disarm. The window SHALL run on the injected scheduler — virtual time in tests, no wall clock.

#### Scenario: Second press within the window quits

- **GIVEN** an armed window after one Ctrl+C on empty input
- **WHEN** a second Ctrl+C arrives within two seconds of virtual time
- **THEN** the action is quit

#### Scenario: Expired window re-arms

- **GIVEN** an armed window
- **WHEN** more than two seconds of virtual time pass before the next Ctrl+C
- **THEN** that press re-arms instead of quitting

### Requirement: Approval prompt

A pending approval SHALL render as a live-area prompt naming the tool and its argument summary with `[y]es`, `[n]o`, `[a]lways this session`. Only bare `y`, `n` and `a` SHALL decide; a stray Enter SHALL deny (nothing runs by accident); all other keys SHALL not decide. The prompt SHALL produce a choice; session memory for "always" is the wiring layer's concern.

#### Scenario: Explicit keys decide

- **GIVEN** a pending approval
- **WHEN** `y`, `n` or `a` is pressed
- **THEN** the choice is approve, deny or always respectively

#### Scenario: Enter denies

- **GIVEN** a pending approval
- **WHEN** Enter is pressed
- **THEN** the choice is deny

### Requirement: Status footer

The footer SHALL show model, tokens used against the context window with percent, working directory and git branch as one dim line, formatted culture-invariantly. It SHALL degrade deterministically at narrow widths (branch dropped first, then directory; model and usage survive) and omit percent when the window is unknown.

#### Scenario: Narrow width degrades in order

- **GIVEN** a footer too wide for the terminal
- **WHEN** it renders
- **THEN** the branch is dropped first, then the directory, and model and usage remain

#### Scenario: No repository, no branch

- **GIVEN** a working directory outside a git repository
- **WHEN** the footer renders
- **THEN** the branch segment is absent and the rest renders normally

### Requirement: Git branch discovery without processes

The branch SHALL be read from `.git` (directory or worktree `gitdir:` file) without spawning any process; detached HEAD SHALL show the short SHA; any read failure SHALL degrade to no branch.

#### Scenario: Detached HEAD shows a short SHA

- **GIVEN** a repository whose HEAD holds a commit id
- **WHEN** the branch is read
- **THEN** the short SHA (first seven characters) is returned
