## MODIFIED Requirements

### Requirement: Tool output truncation

`ToolOutput.Truncate` SHALL bound tool output to a default of 30,000 characters: output at or under the limit SHALL pass through unchanged; longer output SHALL keep the first half and last half of the budget with a marker stating the omitted character count (culture-invariant) and, when a spill file was written, its absolute path; cut points SHALL never split a UTF-16 surrogate pair; limits smaller than the marker SHALL degrade gracefully without throwing. For a session-owned spill file, `read` SHALL return one bounded page at a time, with the complete model-facing result at or below the estimated token bound selected by `output.spillThresholdTokens` using the spill step's UTF-8-bytes-divided-by-four estimate. Each page SHALL contain an exact contiguous text slice from the file and SHALL carry an explicit next offset or line in its result when more content remains; the model SHALL continue with another `read` call using that continuation, not a separate continuation tool or a pointer telling it to find the rest in a file. A read of a known spill file under the session's `tool-output/` directory SHALL be exempt from spill admission. The exemption SHALL match the resolved absolute path of a known spill file, not an arbitrary path prefix. No page from such a read SHALL be replaced by a spill pointer.

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
- **THEN** the full output is written to a file in the session's own directory, the marker names that absolute path, and successive `read` calls using each returned continuation make the full content reachable

#### Scenario: No spill without a session
- **GIVEN** a `--no-session` run and a tool result larger than the limit
- **WHEN** a result is truncated
- **THEN** no file is written and the marker keeps the count-only form

#### Scenario: A failed spill never fails the tool
- **GIVEN** a session directory that cannot be written
- **WHEN** a result over the limit is truncated
- **THEN** the tool result is still delivered with the count-only marker and the failure is reported as a notice, not a tool error

#### Scenario: A spill read is bounded and carries its continuation
- **GIVEN** a known spill file whose content exceeds one page
- **WHEN** the model reads it
- **THEN** the result contains at most the `output.spillThresholdTokens` estimated-token bound and an explicit next offset or line, and does not tell the model to find the remainder in another file

#### Scenario: A spill read is exempt from spill admission
- **GIVEN** a known spill file under the session's `tool-output/` directory
- **WHEN** a page is returned by `read`
- **THEN** the page is returned directly and is not replaced by a spill pointer

#### Scenario: The model reads a spill to completion
- **GIVEN** a session-owned spill file with approximately 50,000 estimated tokens
- **WHEN** the model repeatedly calls `read` using each page's continuation until no continuation remains
- **THEN** every byte of the file is reachable, the pages join to the original text exactly, and no `read` result is a spill pointer
