## ADDED Requirements

### Requirement: Print mode contract

`lunate -p "<prompt>"` SHALL run one prompt to completion without any interactive prompt. In default mode exactly the final answer — the text of the last assistant message with content — SHALL be written to stdout, written once at the end; all diagnostics SHALL go to stderr, so redirecting stdout captures a clean answer. With `--json` every event of the run SHALL be written to stdout as one JSON object per line in arrival order, with the same stamped fields as the event records (`type`, `runId`, `sessionId`, `parentRunId`, `source` when non-default) plus type-specific fields; `tool_call_result` SHALL carry the tool's detail record as a JSON element. Exit codes SHALL be: 0 for a run that finished with the `stop` reason, 1 for a run error or a failure before a clean finish, 2 for `length` and `step_limit` finishes, 130 when cancelled. Usage errors (missing prompt, unknown flags) SHALL exit 2 with a message on stderr and nothing on stdout.

#### Scenario: The final answer is the only thing on stdout

- **GIVEN** a scripted run that calls tools and streams several assistant messages
- **WHEN** print mode runs without `--json`
- **THEN** stdout is exactly the final assistant message text and diagnostics (if any) are on stderr

#### Scenario: JSON mode emits the whole stream

- **GIVEN** a scripted run with a tool call
- **WHEN** print mode runs with `--json`
- **THEN** stdout is one JSON object per event, in arrival order, each parseable on its own line, with `sessionId` present

#### Scenario: Step limit exits 2

- **GIVEN** a scripted run that reaches the step limit
- **WHEN** print mode finishes
- **THEN** the exit code is 2 and the run's `step_limit_reached` event appears in the JSON stream

#### Scenario: Missing prompt is a usage error

- **GIVEN** no prompt argument
- **WHEN** `-p` is passed alone
- **THEN** exit code 2, usage text on stderr, nothing on stdout

### Requirement: Non-interactive tool approval

Print mode SHALL never approve silently beyond the resolved policy: the run SHALL carry a non-interactive approver that maps the `approval` setting onto tool risk — `ask` (default) allows read-only tools and denies file writes and commands; `auto-edit` also allows file writes and edits and denies commands; the per-run `--yolo` flag allows everything. The setting SHALL never accept `yolo`. A denied call SHALL become an error tool result the model can read, and SHALL produce one diagnostic line on stderr naming the tool and the policy. The interactive tracked-file refinement of the `ask` level lands with the approval flow; print mode's approximation SHALL be documented.

#### Scenario: Ask denies a write

- **GIVEN** approval `ask` and a scripted run that tries to write a file
- **WHEN** the write tool call executes
- **THEN** it is denied with an error result, the file is untouched, and stderr names the tool and policy

#### Scenario: Auto-edit allows edits and denies commands

- **GIVEN** approval `auto-edit` and a scripted run that edits a file and then runs a command
- **WHEN** the run proceeds
- **THEN** the edit executes and the command is denied unless `--yolo` was passed

#### Scenario: Yolo allows everything

- **GIVEN** `--yolo`
- **WHEN** a scripted run writes and runs commands
- **THEN** both execute and no approval denial occurs

#### Scenario: Yolo cannot be saved

- **GIVEN** a settings file with `"approval": "yolo"`
- **WHEN** settings are resolved
- **THEN** validation fails with an error saying yolo is per-run only
