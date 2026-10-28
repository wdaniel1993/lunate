# Loop deltas — context hygiene

## MODIFIED Requirements

### Requirement: Tool execution safety

Unknown tools, malformed arguments and exceptions from a tool SHALL become error results with instructing messages, never exceptions out of the stream. Arguments that cannot be assembled — a call still carrying raw `$arguments` fragments or a failed assembly, from the pipeline's accumulator or from a raw streaming client — SHALL never reach a tool: `ToolCallArgs` SHALL carry the raw argument text when it exists, falling back to the failure message otherwise, and the result SHALL instruct the model to fix the arguments. Tool output SHALL be admitted by the context-admission path — masked, projected through an optional `FullOutput`, spilled over the token threshold and bounded to a pointer — before it enters the history; `Details` SHALL remain UI-only; a decline from the approver SHALL become an error result, and an approver that fails SHALL produce an approval-specific error result, not one blaming the tool. The approval seam SHALL carry the asking run (run id, parent run id, depth) so prompts can label a subagent as the asker, and "always this session" decisions SHALL apply across the whole run tree of the session. The event sink passed to tools SHALL accept only extension events; anything else SHALL be rejected and surface as a tool error.

#### Scenario: Tool failure keeps the history valid

- **GIVEN** a scripted tool that throws
- **WHEN** the run executes
- **THEN** the model receives an error result naming what failed, and the run can continue

#### Scenario: Unassembled arguments never reach a tool

- **GIVEN** a call whose arguments are raw fragments or failed to assemble, from the pipeline's accumulator or from a raw streaming client
- **WHEN** the run executes
- **THEN** `ToolCallArgs` carries the raw argument text, no tool executes, and the model receives an instructing error result

#### Scenario: Long output is bounded

- **GIVEN** a tool whose output exceeds the budget
- **WHEN** its result enters the history
- **THEN** the model-facing output carries the pointer and the full output stays out of the model's history

#### Scenario: Admission precedes the history

- **GIVEN** a tool result over the spill threshold
- **WHEN** the loop appends it
- **THEN** the `ToolCallResult` event, the history message and the session entry all carry the same masked pointer text, and none carries the pre-admission text

#### Scenario: Approver failure is reported as an approval error

- **GIVEN** an approver that throws
- **WHEN** the tool call is considered
- **THEN** the error result names the approval failure, not a tool failure

#### Scenario: Tools cannot emit loop events

- **GIVEN** a tool that emits a non-extension event through its context
- **WHEN** it runs
- **THEN** the emission is rejected and the tool call becomes an error result

### Requirement: Compaction

The loop SHALL compact the request before a model call when the estimated request size passes 80% of the model's context window — estimated as `chars / 4`, corrected by the last reported input-token usage when available, and measured over the request as it will be sent (after masking, `FullOutput` projection, spill pointers and stale-read notices) — or when compaction is requested explicitly. Compaction SHALL summarize the history older than the kept tail (system prompt, AGENTS.md content, and the last N turns, N configurable, default 4) with the same model and a fixed summarization prompt covering goal, decisions, files touched and open problems. A tool call SHALL never be split from its result — the tail boundary SHALL expand to pair boundaries. The session file SHALL keep the full history; compaction SHALL be recorded as a `compaction` entry (`summary`, `replaces` = the replaced entry ids); in-run compaction SHALL emit `CompactionApplied` with the run id, replaced entry ids and the estimated tokens after compaction (an explicitly requested out-of-run compaction records the entry and rebuilds history — there is no run to carry an event). The rebuilt request SHALL be the system prompt plus the summary plus the kept tail. Compactor failure SHALL leave the request unchanged and SHALL be reported (the next request may retry). When the model window is unknown, a documented default SHALL apply and the run SHALL proceed. An image part SHALL count as `max(256, width × height / 750)` estimated tokens for the trigger, SHALL render as its `[image <w>x<h>]` placeholder in the summarization transcript, and SHALL NOT send its bytes to the summarizer. Compaction SHALL NOT delete image blobs. The session SHALL NOT store the estimate: the recorded usage is the provider's count for the request as built.

#### Scenario: A long session compacts once

- **GIVEN** a recorded long session that passes the threshold
- **WHEN** the next model request is composed
- **THEN** it compacts exactly once, the next request is under 60% of the window, a `compaction` entry records the summary and replaced ids, `CompactionApplied` is emitted, and replay still matches

#### Scenario: Tool pairs are never split

- **GIVEN** a tail boundary falling between a tool call and its result
- **WHEN** compaction runs
- **THEN** the boundary expands so the pair stays together in the kept tail

#### Scenario: Compactor failure leaves the request unchanged

- **GIVEN** a summarization call that fails
- **WHEN** compaction is attempted
- **THEN** the request is sent unchanged and the failure is reported

#### Scenario: Resume after compaction

- **GIVEN** a session file containing a compaction entry
- **WHEN** the session is loaded and its history reconstructed
- **THEN** the history is the summary plus the messages after the replaced entries

#### Scenario: Images count toward the trigger

- **GIVEN** a history with images whose placeholder text alone is small
- **WHEN** the request size is estimated
- **THEN** each image adds `max(256, width × height / 750)` tokens and compaction triggers on that estimate

#### Scenario: The summarizer never receives image bytes

- **GIVEN** a history containing an image part
- **WHEN** the summarization request is built
- **THEN** the transcript carries `[image <w>x<h>]` and no image data

#### Scenario: Admission moves the trigger later

- **GIVEN** a history whose only large item is a tool result over the spill threshold and whose unprojected size would pass 80% of the window
- **WHEN** the request is composed
- **THEN** the estimate is over the pointer text as sent, no compaction fires, and the history is unchanged

## ADDED Requirements

### Requirement: Context admission

Every tool result SHALL pass one admission path before it enters the history: mask (when a mask is configured), project a tool-authored `FullOutput`, spill over the threshold, and bound the in-band text to a preview and pointer. The `ToolCallResult` event, the history message and the session entry SHALL carry the same admitted text; the spill file SHALL carry the masked full text. Masking SHALL run before spilling, so no raw value is written to disk or emitted. The loop SHALL NOT rewrite a tool's answer beyond masking and the bound, and SHALL NOT reorder, summarise or drop a result on its own judgement. A superseded read result SHALL be replaced in the in-memory history, so the next request and the compaction estimate both see the one-line notice while the session entry keeps the original text.

#### Scenario: One text reaches every consumer

- **GIVEN** a tool result over the threshold with a configured mask
- **WHEN** the result is admitted
- **THEN** the event, the request and the session entry are identical, the spill file holds the masked full text, and the pre-mask text is in none of them

#### Scenario: Admission is not a summarizer

- **GIVEN** a tool whose `Output` is three lines and whose `FullOutput` is a transcript
- **WHEN** the result is admitted
- **THEN** the request contains exactly those three lines and the pointer

#### Scenario: Stale read results are replaced in the history

- **GIVEN** a history containing a read result that a later mutation superseded
- **WHEN** the next request is built
- **THEN** the request carries the one-line stale notice instead of the read text, while the session entry is unchanged and a resumed load still sees the original
