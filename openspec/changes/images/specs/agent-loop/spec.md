## ADDED Requirements

### Requirement: Image parts in model requests

The harness SHALL store every image attachment through the session's image store and SHALL append a session message whose image parts are `ImageContentReference` values (digest, mime, width, height), never image bytes. Before each model request it SHALL hydrate those references into Microsoft.Extensions.AI `DataContent` parts for the provider, reading the blob from the session store or the in-memory store of a no-session run. A tool result carrying `Images` SHALL append its tool-role message with the placeholder text and then one user-role message carrying the image contents with the text `Image from <tool>: [image <w>x<h>].`; the follow-up user message is the portable carrier because the OpenAI chat-completions adapter maps image content only in user-role messages, not in tool-role results. A resumed session SHALL hydrate the same way. Hydration SHALL require the active model to declare `supportsVision`; when it does not, the harness SHALL fail the run before the request, naming the model id and the number of images in the request, and SHALL NOT drop them. A missing referenced blob SHALL fail the run before the first request, naming the digest and the path; the request SHALL NOT silently drop the image.

#### Scenario: A read image becomes a tool message and a user image message

- **GIVEN** a `read` call returning `[image 1024x768]` and PNG bytes
- **WHEN** the loop appends the result
- **THEN** the session gains the tool message with the placeholder and one user message whose contents carry the image reference, and the next request carries `DataContent`

#### Scenario: Resume rehydrates the image

- **GIVEN** a schema 3 session referencing a stored blob
- **WHEN** the session is resumed and a request is built
- **THEN** the request carries the image bytes as `DataContent` and the session file still carries only the reference

#### Scenario: Resume on a text-only model fails

- **GIVEN** a schema 3 session referencing a stored blob and a model without vision support
- **WHEN** the session is resumed and a request is built
- **THEN** the run fails naming the model id and the image count before any request, and no request carries the image

#### Scenario: A missing blob stops the run

- **GIVEN** a session referencing a digest whose blob is missing
- **WHEN** the harness is built for the run
- **THEN** it fails naming the digest and expected path before any request

#### Scenario: A no-session run keeps images in memory

- **GIVEN** a `--no-session` run with an attached image
- **WHEN** the request is built
- **THEN** the image reaches the provider from memory and no file is written

## MODIFIED Requirements

### Requirement: Compaction
The loop SHALL compact the request before a model call when the estimated request size passes 80% of the model's context window — estimated as `chars / 4`, corrected by the last reported input-token usage when available — or when compaction is requested explicitly. Compaction SHALL summarize the history older than the kept tail (system prompt, AGENTS.md content, and the last N turns, N configurable, default 4) with the same model and a fixed summarization prompt covering goal, decisions, files touched and open problems. A tool call SHALL never be split from its result — the tail boundary SHALL expand to pair boundaries. The session file SHALL keep the full history; compaction SHALL be recorded as a `compaction` entry (`summary`, `replaces` = the replaced entry ids); in-run compaction SHALL emit `CompactionApplied` with the run id, replaced entry ids and the estimated tokens after compaction (an explicitly requested out-of-run compaction records the entry and rebuilds history — there is no run to carry an event). The rebuilt request SHALL be the system prompt plus the summary plus the kept tail. Compactor failure SHALL leave the request unchanged and SHALL be reported (the next request may retry). When the model window is unknown, a documented default SHALL apply and the run SHALL proceed. An image part SHALL count as `max(256, width × height / 750)` estimated tokens for the trigger, SHALL render as its `[image <w>x<h>]` placeholder in the summarization transcript, and SHALL NOT send its bytes to the summarizer. Compaction SHALL NOT delete image blobs.

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
