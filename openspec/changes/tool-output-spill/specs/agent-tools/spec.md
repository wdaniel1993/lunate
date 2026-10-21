# Tool output delta — spill to a file

## MODIFIED Requirements

### Requirement: Tool output truncation
`ToolOutput.Truncate` SHALL bound tool output to a default of 30,000 characters: output at or under the limit SHALL pass through unchanged; longer output SHALL keep the first half and last half of the budget with a marker stating the omitted character count (culture-invariant) and, when a spill file was written, its absolute path; cut points SHALL never split a UTF-16 surrogate pair; limits smaller than the marker SHALL degrade gracefully without throwing.

#### Scenario: Short output passes through
- **GIVEN** output within the limit
- **WHEN** it is truncated
- **THEN** the same string is returned unchanged

#### Scenario: Long output is cut in the middle with a marker
- **GIVEN** output longer than the limit
- **WHEN** it is truncated
- **THEN** the result contains the head, the marker with the omitted count and the tail, and the omitted count matches the removed characters

#### Scenario: Cut points never split surrogate pairs
- **GIVEN** output whose cut points fall inside an emoji
- **WHEN** it is truncated
- **THEN** the result contains no lone surrogate halves

#### Scenario: The full output survives in a spill file

- **GIVEN** a session run and a tool result larger than the configured limit
- **WHEN** the result is truncated
- **THEN** the full output is written to a file in the session's own directory, the marker names that absolute path, and `read` on it returns the full content

#### Scenario: No spill without a session

- **GIVEN** a `--no-session` run and a tool result larger than the limit
- **WHEN** the result is truncated
- **THEN** no file is written and the marker keeps the count-only form

#### Scenario: A failed spill never fails the tool

- **GIVEN** a session directory that cannot be written
- **WHEN** a result over the limit is truncated
- **THEN** the tool result is still delivered with the count-only marker and the failure is reported as a notice, not a tool error
