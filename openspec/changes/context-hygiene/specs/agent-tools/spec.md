# Tool deltas — context hygiene

## MODIFIED Requirements

### Requirement: Tool contract

`Lunate.Agent` SHALL define tools as its own type: `ITool` with a name, a description, a hand-written JSON `ParametersSchema`, a `ToolRisk` (`ReadOnly`, `Write`, `Execute`) and `ExecuteAsync(JsonElement args, ToolContext ctx, CancellationToken ct)`; results as `ToolResult` (output, error flag, UI-only `Details`); and `ToolContext` carrying the working directory, an event sink that accepts only extension events, and the per-run subagent seam (`Subagents`, see the agent-loop specification — it starts child runs with an optional allow-list of the run's own tools and only-tightening limits) stamped by the harness alongside the run id and file-change sink. Schemas SHALL be authored by hand, never built by reflection. Error results SHALL reach the model through their instructing output text; a provider-level error flag is not sent (revisit when real tools and a live fixture exist). `ToolContext` SHALL also carry the active `ModelInfo` when the run has one, so a tool can decide whether the model supports a capability; it SHALL be null outside a run. `ToolResult` SHALL optionally carry `Images`, a list of `ImageAttachment` values (media type, width, height and bytes); `Output` SHALL remain the only in-band model-facing text and SHALL contain the `[image <w>x<h>]` placeholder for an image result, never image bytes. `ToolResult` SHALL optionally carry `FullOutput`, the complete text behind a concise `Output`; `Output` SHALL remain the result's only in-band model-facing text, and the loop SHALL spill `FullOutput` and name it in the pointer rather than send its body. The loop SHALL give the core file tools a view of the run's read ledger through `ToolContext`, null outside a run; it is how a file-reading tool records the version it served and how a file-mutating tool asks whether that version is still current.

#### Scenario: Contract is implementable without reflection

- **GIVEN** a tool implemented in a test with a hand-written schema
- **WHEN** it is exercised through the contract types
- **THEN** no reflection is involved and the result carries output, error flag and optional UI-only details

#### Scenario: Details never travel to the model

- **GIVEN** a tool result with `Details` set
- **WHEN** the loop appends the tool result to the conversation
- **THEN** only the output reaches the model — instructing text for error results — while details stay UI-only and the error flag feeds events and telemetry

#### Scenario: The subagent seam is stamped per run

- **GIVEN** a tool executed by the loop
- **WHEN** it reads its context
- **THEN** `Subagents` exposes the run's runner (its `Depth` being the run's spawn depth, shared by the run's parallel calls), and a tool executed outside the loop sees `Subagents` as null

#### Scenario: The active model is visible to a tool

- **GIVEN** a run on a catalog model with vision
- **WHEN** a tool reads its context
- **THEN** `Model` names that model and reports `SupportsVision` as true; outside a run it is null

#### Scenario: An image result carries bytes beside the placeholder

- **GIVEN** a tool returning an image attachment
- **WHEN** its result is inspected
- **THEN** `Output` is `[image <w>x<h>]`, `Images` holds the bytes and media type, and `Details` is unchanged

#### Scenario: The read ledger is stamped only inside a run

- **GIVEN** a tool executed by the loop and the same tool executed directly with a hand-built context
- **WHEN** each reads the ledger view from its context
- **THEN** the loop-run context carries the run's ledger and the hand-built context carries null

### Requirement: Tool output truncation

Every tool result SHALL be admitted through one path before it enters the model's history. A result whose model-facing text exceeds `output.spillThresholdTokens` (default 2,000 estimated tokens, where a token is estimated as UTF-8 bytes / 4) or `output.toolResultLimit` characters SHALL have its full model-facing text written to the session's spill file and SHALL be sent as a bounded preview plus a pointer. The preview SHALL keep at most the first 10 and last 10 lines within 2,000 characters and the pointer SHALL state the omitted character count, the estimated tokens, the line count and the absolute spill path (all counts culture-invariant) and SHALL tell the model to read the file with `offset` and `limit`. The spill file SHALL live at `<sessions-dir>/<session-id>/tool-output/<run>-<seq>.txt`, owner-only, LF, UTF-8 without BOM, with a 64 MB ceiling; `read` SHALL serve it for the owning session and for a resume of that session. A result at or under both limits SHALL pass through unchanged. Without a session no file SHALL be written, and the text SHALL be bounded by `output.toolResultLimit` with the head/tail marker. A failed write SHALL never fail the tool: the result is still delivered and the failure is a notice. Cut points SHALL never split a UTF-16 surrogate pair; limits smaller than the marker SHALL degrade gracefully without throwing.

#### Scenario: Short output passes through

- **GIVEN** output within both limits
- **WHEN** it is admitted
- **THEN** the same string is returned unchanged

#### Scenario: Long output is cut in the middle with a marker

- **GIVEN** output longer than the limit that cannot be spilled (no session, or the write failed)
- **WHEN** it is bounded
- **THEN** the result contains the head, the marker with the omitted count and the tail, and the omitted count matches the removed characters

#### Scenario: The full output survives in a spill file

- **GIVEN** a session run and a tool result larger than the configured threshold
- **WHEN** the result is admitted
- **THEN** the full text is written to a file in the session's own directory, the pointer names that absolute path, and `read` on it returns the full content

#### Scenario: A result over the token threshold spills under the character cap

- **GIVEN** a session run, a 9,000-character tool result and the default limits
- **WHEN** the result is admitted
- **THEN** the full text is in the spill file, the in-band text is a preview and pointer, and the request is under 700 estimated tokens

#### Scenario: The pointer names the file and what it omitted

- **GIVEN** a spilled result
- **WHEN** the in-band text is inspected
- **THEN** it states the omitted characters, the estimated tokens, the line count and the absolute path, and tells the model to read with offset and limit

#### Scenario: The model can read the spill file

- **GIVEN** a spill file written by the session's run
- **WHEN** the model calls `read` on the path from the marker
- **THEN** the call succeeds and returns the full text, and a `read` of a path outside the session's `tool-output` directory is still refused

#### Scenario: A resumed session can read it too

- **GIVEN** a session with a spilled result, loaded again
- **WHEN** its history is reconstructed and `read` opens the marker's path
- **THEN** the file still holds the full text and the read succeeds

#### Scenario: A result over the character cap also spills

- **GIVEN** a result longer than `output.toolResultLimit` but with a high token threshold
- **WHEN** it is admitted
- **THEN** it spills and the pointer says so, rather than falling back to a 30,000-character body

#### Scenario: No spill without a session

- **GIVEN** a `--no-session` run and a result over both limits
- **WHEN** the result is admitted
- **THEN** no file is written and the in-band text keeps the head/tail marker

#### Scenario: A failed spill never fails the tool

- **GIVEN** a session directory that cannot be written
- **WHEN** a result over the threshold is admitted
- **THEN** the tool result is still delivered and the failure is reported as a notice, not a tool error

#### Scenario: Cut points never split surrogate pairs

- **GIVEN** output whose cut points fall inside an emoji
- **WHEN** it is bounded
- **THEN** the result contains no lone surrogate halves

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

## ADDED Requirements

### Requirement: Concise tool results

A tool whose model-facing answer is a projection of a larger transcript SHALL set `ToolResult.FullOutput` to the transcript and return the answer in `Output`. The loop SHALL spill the masked `FullOutput` and append a pointer to the answer; it SHALL NOT derive concision from the content, rewrite the answer, or drop `FullOutput` when the result is an error. A `FullOutput` equal to `Output` SHALL behave as if it were absent. Without a session the loop SHALL append the bounded `FullOutput` after the answer so no text is silently lost.

#### Scenario: A transcript-heavy command sends its answer

- **GIVEN** a `bash` call that prints 20,000 characters to stdout and one failure line to stderr
- **WHEN** the result is admitted
- **THEN** the request carries stderr, the exit footer and the pointer (under 400 estimated tokens), and the spill file carries the full stdout and stderr

#### Scenario: The loop does not guess the answer

- **GIVEN** a tool whose `Output` contains a line resembling an error while its `FullOutput` holds the rest
- **WHEN** the result is admitted
- **THEN** the request carries exactly the tool's `Output` (masked) and the pointer; the loop adds no line of its own choosing

#### Scenario: An equal full output is absent

- **GIVEN** a tool that sets `FullOutput` to the same text as `Output`
- **WHEN** the result is admitted
- **THEN** no spill file is written and the result behaves as before

#### Scenario: Without a session nothing is lost

- **GIVEN** a `--no-session` run and a tool that sets a large `FullOutput`
- **WHEN** the result is admitted
- **THEN** the request carries the answer and the bounded transcript with the count-only marker, and no pointer to a file that does not exist

### Requirement: Stale reads

The loop SHALL keep one read ledger per harness, covering the history it carries, and SHALL stamp a view of it into each run's `ToolContext`. A successful text `read` SHALL record the canonical path, a SHA-256 digest of the file's LF-normalised text and the tool call id. The newest read of a path SHALL be the live version; every earlier read result of that path SHALL be superseded. `edit` and `write` SHALL compare the digest of the text they read (inside the file mutation queue) with the live version and SHALL refuse with an instructing error result when the live version was invalidated by a published mutation (from the file tools or bash) or when the digest differs because the file changed out of band. The refusal SHALL name the path, say the file changed after the last read, and tell the model to re-read and base `old_text` on the current text. A path with no read record SHALL NOT be refused, and tools executed outside a run SHALL keep today's behaviour. On resume the ledger SHALL be seeded from the loaded history with unknown versions, so the first edit of a previously read path requires a re-read. Superseded read results SHALL be replaced in the in-memory history that builds the next request by a one-line notice naming the path, so the request and the compaction estimate both see it; the session file SHALL keep the original entry.

#### Scenario: An edit based on a read before the edit is refused

- **GIVEN** a run that read a file and then edited it successfully
- **WHEN** the model edits the same file again from its memory of the first read
- **THEN** the second edit is refused, the message names the path and says it changed after the last read, and the file is untouched

#### Scenario: A re-read allows the edit

- **GIVEN** a refused stale edit
- **WHEN** the model reads the file again and retries with the current text
- **THEN** the edit succeeds

#### Scenario: An out-of-band change is caught

- **GIVEN** a read and a change written by a process that publishes no file-change event
- **WHEN** the model edits using the pre-change text
- **THEN** the digest mismatch refuses the edit with the same instructing message

#### Scenario: The stale copy leaves the request

- **GIVEN** a run that read 2,000 lines and then edited the file
- **WHEN** the next request is built
- **THEN** the old read result is a one-line stale notice, the session entry still holds the original text, and the request is at least 4,000 estimated tokens smaller

#### Scenario: Resume requires one re-read

- **GIVEN** a resumed session whose history contains a read of a file
- **WHEN** the model edits that file before reading it again
- **THEN** the edit is refused and asks for a re-read

#### Scenario: A never-read path is unchanged

- **GIVEN** a run that has not read a file
- **WHEN** the model edits it with text that matches
- **THEN** the edit succeeds exactly as before

## MODIFIED Requirements

### Requirement: Masking tool results

With `AgentHarnessOptions.ToolOutputMask` set, the loop SHALL apply it to every tool result's model-facing text before the result is truncated, so a value that crosses a truncation boundary cannot send a fragment. The masked text SHALL be what enters the history, the emitted `ToolCallResult`, the session entry and the spill file. The mask SHALL be exact-value only: a value Lunate holds is replaced by `[redacted:<name>]`, and a value Lunate does not hold SHALL be left unchanged. A null mask SHALL be identity. The host SHALL supply the same exact-value redactor for session and `--no-session` runs, and the mask SHALL be idempotent with the redaction at rest.

#### Scenario: A known credential never reaches the provider

- **GIVEN** a session whose redactor holds `OPENAI_API_KEY` and a tool that echoes that value
- **WHEN** the result is admitted
- **THEN** the request, the `ToolCallResult` event, the session entry and the spill file carry `[redacted:OPENAI_API_KEY]`, and the raw value appears in none of them

#### Scenario: A value across a truncation boundary is masked before the cut

- **GIVEN** a tool result over the character cap whose known credential value straddles the head or the tail cut
- **WHEN** the result is masked and then truncated
- **THEN** the truncated text carries the marker and no fragment of the value, in the request and in the event

#### Scenario: A no-session run masks too

- **GIVEN** a `--no-session` run on a machine whose environment holds a provider key and a tool that echoes it
- **WHEN** the result is admitted
- **THEN** the request carries the marker, even though no session file exists

#### Scenario: Exact value only

- **GIVEN** a tool result containing a token-shaped string that is not a credential Lunate holds
- **WHEN** the result is admitted
- **THEN** the string is unchanged
