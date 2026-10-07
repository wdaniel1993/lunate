## ADDED Requirements

### Requirement: Compaction
The loop SHALL compact the request before a model call when the estimated request size passes 80% of the model's context window — estimated as `chars / 4`, corrected by the last reported input-token usage when available — or when compaction is requested explicitly. Compaction SHALL summarize the history older than the kept tail (system prompt, AGENTS.md content, and the last N turns, N configurable, default 4) with the same model and a fixed summarization prompt covering goal, decisions, files touched and open problems. A tool call SHALL never be split from its result — the tail boundary SHALL expand to pair boundaries. The session file SHALL keep the full history; compaction SHALL be recorded as a `compaction` entry (`summary`, `replaces` = the replaced entry ids) and SHALL emit `CompactionApplied` with the run id, replaced entry ids and the estimated tokens after compaction. The rebuilt request SHALL be the system prompt plus the summary plus the kept tail. Compactor failure SHALL leave the request unchanged and SHALL be reported (the next request may retry). When the model window is unknown, a documented default SHALL apply and the run SHALL proceed.

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
