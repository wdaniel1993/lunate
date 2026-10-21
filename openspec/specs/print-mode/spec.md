# print-mode Specification

## Purpose
Print mode is the non-interactive frontend: it runs one prompt to completion and exits with a clean contract — the final answer on stdout, the full event stream as JSON lines with `--json`, diagnostics on stderr, and semantic exit codes. Because nobody can be asked for approval, it maps the resolved approval policy onto tool risk deterministically (`--yolo` is the per-run escape hatch), so unattended runs are scriptable without silently approving beyond the policy.

## Requirements

### Requirement: Print mode contract

`lunate -p "<prompt>"` SHALL run one prompt to completion without any interactive prompt. In default mode exactly the final answer — the text of the last assistant message with content — SHALL be written to stdout, written once at the end; all diagnostics SHALL go to stderr, so redirecting stdout captures a clean answer. With `--json` every event of the run SHALL be written to stdout as one JSON object per line in arrival order, reasoning events included (`reasoning_start`, `reasoning_message_content`, `reasoning_end`), with the same stamped fields as the event records (`type`, `runId`, `sessionId`, `parentRunId`, `source` when non-default) plus type-specific fields; `tool_call_result` SHALL carry the tool's detail record as a JSON element. Exit codes SHALL be: 0 for a run that finished with the `stop` reason, 1 for a run error or a failure before a clean finish, 2 for `length` and `step_limit` finishes, 130 when cancelled. Usage errors (missing prompt, unknown flags) SHALL exit 2 with a message on stderr and nothing on stdout.

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

#### Scenario: Reasoning events appear in the JSON stream

- **GIVEN** a scripted run whose model streams reasoning
- **WHEN** print mode runs with `--json`
- **THEN** `reasoning_start`, `reasoning_message_content` and `reasoning_end` objects appear in arrival order like every other event

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

### Requirement: Cache usage in JSON output

With `--json`, a `usage_updated` object SHALL carry the provider's prompt-cache counts alongside the existing `usage` object: `cacheReadTokens` from `UsageDetails.CachedInputTokenCount` and `cacheWriteTokens` from `UsageDetails.AdditionalCounts["CacheCreationInputTokens"]`, each emitted only when the provider reported a positive count. A reported zero SHALL be treated as unknown and omitted, and the two counts SHALL never be synthetic zeros. The `usage` object itself SHALL keep its existing shape.

#### Scenario: A usage event carries both counts

- **GIVEN** a run whose provider reports `CachedInputTokenCount = 1200` and a cache creation count of `300`
- **WHEN** print mode runs with `--json`
- **THEN** its `usage_updated` line has `cacheReadTokens: 1200` and `cacheWriteTokens: 300` next to the unchanged `usage` object

#### Scenario: Unknown counts are absent

- **GIVEN** a provider that reports only input and output tokens
- **WHEN** print mode runs with `--json`
- **THEN** the `usage_updated` line has neither cache field

#### Scenario: A reported zero is treated as unknown

- **GIVEN** a provider that reports `CachedInputTokenCount = 0` and a cache creation count of `0`
- **WHEN** print mode runs with `--json`
- **THEN** the `usage_updated` line has neither cache field, while the nested raw `usage` object still carries the provider's `cachedInputTokenCount: 0`

#### Scenario: The default output is unchanged

- **GIVEN** print mode without `--json`
- **WHEN** a run reports cache counts
- **THEN** stdout still carries only the final assistant text

### Requirement: Cost in JSON output

With `--json`, a `usage_updated` object SHALL carry a numeric `costUsd` when the record's model is in the catalog and every token lane with a positive count has a known price. The value SHALL be the record's cost in US dollars, computed with `decimal` and invariant arithmetic. A record whose model is unknown or unpriced SHALL omit the field rather than emit `0`; a zero-token priced record MAY carry `0`. The existing `usage` object and the cache fields SHALL keep their shape. The field SHALL be absent from the non-JSON output, which still carries only the final answer on stdout.

#### Scenario: A priced record carries its cost

- **GIVEN** a `usage_updated` record for a priced model with input, output and cache tokens
- **WHEN** `--json` serializes it
- **THEN** the line carries `costUsd` beside the unchanged `usage` object

#### Scenario: An unpriced model omits the field

- **GIVEN** a `usage_updated` record whose model has no pricing
- **WHEN** `--json` serializes it
- **THEN** the line has no `costUsd` field

#### Scenario: An unknown cache rate suppresses a used lane's cost

- **GIVEN** a record with positive cache-read tokens and no cache-read rate
- **WHEN** `--json` serializes it
- **THEN** the line has no `costUsd` field

#### Scenario: The default output is unchanged

- **GIVEN** print mode without `--json`
- **WHEN** a run reports priced usage
- **THEN** stdout still carries only the final assistant text and no cost field
