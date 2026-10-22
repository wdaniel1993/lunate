## ADDED Requirements

### Requirement: Bash mode

At the start of an empty input, `!` SHALL enter bash mode and `!!` SHALL enter it in the silent form; the prompt SHALL render `! ` in the accent colour while the mode is active, and Esc SHALL leave the mode without running, restoring the text with its prefix. Enter SHALL run the remaining line in the session's working directory through the `bash` tool's executor, so the approval policy, shell resolution, environment, timeout, output bounds and process-tree rules are the tool's; the run SHALL commit one tool block whose header carries the exit code and the duration (culture-invariant), and no model request SHALL be made. A `!` that is not the first character of the input SHALL stay literal. A normal `!` submission SHALL add the command and its result to the conversation; a `!!` submission SHALL add neither. Bash lines SHALL be recalled only from bash mode's own history stream, and prompt history SHALL remain unchanged. A `!` submission while a turn is running SHALL be refused with a notice naming the run instead of queueing or steering. Print mode SHALL have no bash mode.

#### Scenario: A command runs and commits a block

- **GIVEN** an idle prompt
- **WHEN** the user types `!echo hi` and presses Enter
- **THEN** the prompt was rendered as `! `, the command runs in the workspace, one tool block commits with `hi` in its body and exit 0 and a duration in its header, and no model request is made

#### Scenario: Approval still decides

- **GIVEN** an approval policy that asks before `Execute` calls
- **WHEN** the user submits `!rm -rf build`
- **THEN** the approval prompt appears before anything runs, and a denial leaves the workspace untouched

#### Scenario: The silent form stays out of the conversation

- **GIVEN** a conversation with messages
- **WHEN** the user submits `!!date`
- **THEN** the block commits to the scrollback and the conversation the next request carries is unchanged

#### Scenario: Escape leaves without running

- **GIVEN** bash mode with `!git status` typed
- **WHEN** Esc is pressed
- **THEN** the mode ends, the text is restored with its prefix, and nothing runs

#### Scenario: A mid-text exclamation stays literal

- **GIVEN** an empty prompt
- **WHEN** the user types `explain the ! operator` and presses Enter
- **THEN** the line is submitted as a prompt and no command runs

#### Scenario: Bash history is separate

- **GIVEN** an earlier `!git status` in this session
- **WHEN** Up is pressed in bash mode, then in prompt mode
- **THEN** bash mode recalls the command and prompt mode recalls only prompts

#### Scenario: A running turn refuses the mode

- **GIVEN** a turn in progress
- **WHEN** the user submits `!ls`
- **THEN** a notice names the running turn, nothing runs, and no command is queued or steered
