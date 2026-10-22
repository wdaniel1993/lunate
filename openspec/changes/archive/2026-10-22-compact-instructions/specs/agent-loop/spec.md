## ADDED Requirements

### Requirement: Compaction focus instructions

`/compact` SHALL accept optional trailing text and pass it to the summarization call as focus instructions: the fixed summarization sections (goal, decisions, files touched, open problems) SHALL remain and the focus text SHALL be appended as a separate labelled block. The focus SHALL apply to that compaction only — it SHALL NOT be stored in the session, recorded on the `compaction` entry, or used by any later compaction, including an automatic one. An empty or whitespace-only argument SHALL behave exactly as `/compact` today. An argument longer than 1,000 characters SHALL be refused with a notice naming the limit, and nothing SHALL be compacted.

#### Scenario: Focus reaches the summarizer beside the fixed sections

- **GIVEN** a session with compactable history
- **WHEN** the user runs `/compact focus on the flaky test`
- **THEN** the summarization request contains the focus text and the goal, decisions, files and open-problems sections, and the resulting summary is recorded normally

#### Scenario: Focus does not outlive its call

- **GIVEN** a manual compaction that used focus instructions
- **WHEN** a later automatic compaction runs in the same session
- **THEN** its request contains none of the earlier focus text

#### Scenario: Focus is not persisted

- **GIVEN** a compaction that used focus instructions
- **WHEN** the session file is read
- **THEN** the `compaction` entry holds `summary` and `replaces` only and the focus text appears nowhere in the file

#### Scenario: An over-long argument is refused

- **GIVEN** an argument of 1,001 characters
- **WHEN** `/compact` is submitted
- **THEN** a notice names the 1,000-character limit, no summarization call is made, and the history is unchanged

#### Scenario: An empty argument is the old behaviour

- **GIVEN** compactable history
- **WHEN** `/compact` is submitted with no argument or only spaces
- **THEN** the fixed prompt is used and the summary is recorded as before
