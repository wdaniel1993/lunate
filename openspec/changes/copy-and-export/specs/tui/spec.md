## Purpose

A copy route that works where the session runs, and an export that produces one self-contained document from a session file, without a second source of truth for either.

## ADDED Requirements

### Requirement: Copy the last answer

`/copy` and `Ctrl+X` SHALL write the final assistant message of the most recent completed turn to the clipboard. When the most recent turn ended in an error or a cancellation, the last completed answer before it SHALL be used; when no turn has completed, the command SHALL report that there is nothing to copy and SHALL write nothing.

The clipboard transport SHALL depend on where the session runs. When `SSH_CONNECTION` or `SSH_TTY` is present, the answer SHALL be written as an OSC 52 sequence and no local clipboard tool SHALL be invoked. Otherwise the platform tool SHALL be tried first (`pbcopy`, `wl-copy`, `xclip -selection clipboard`, `clip`), with OSC 52 as the fallback when none exists. The notice SHALL name the transport used and the character count.

An answer larger than 100 KB SHALL be refused with a notice naming the size; it SHALL NOT be truncated and pasted as if complete.

#### Scenario: Copy in a local session

- **GIVEN** a completed turn whose answer is 1,842 characters and a session with no ssh variables
- **WHEN** `/copy` runs
- **THEN** the platform clipboard tool receives the exact answer text and the notice names that tool and the character count

#### Scenario: Copy over ssh never touches the local clipboard

- **GIVEN** `SSH_CONNECTION` is set
- **WHEN** `/copy` runs
- **THEN** an OSC 52 sequence carries the answer and no `pbcopy`, `wl-copy`, `xclip` or `clip` process is started

#### Scenario: Nothing to copy

- **GIVEN** a session where no turn has completed
- **WHEN** `/copy` runs
- **THEN** a notice says there is no completed answer and the clipboard is untouched

#### Scenario: An oversized answer is refused, not truncated

- **GIVEN** an answer of 140 KB
- **WHEN** `/copy` runs
- **THEN** a notice names the size and nothing is written to the clipboard

### Requirement: Export the session

`/export jsonl [path]` SHALL copy the session file byte for byte. `/export html [path]` SHALL write one self-contained HTML document built from the session's entries: messages, tool calls with their arguments and results, diffs, thinking blocks, model and turn boundaries, each with its timestamp. The document SHALL use inline CSS, SHALL contain no JavaScript and no external asset, and SHALL open offline.

Without a path the file SHALL be `<session-id>.<ext>` in the current directory. An existing file SHALL be refused unless `--force` is passed. The confirmation SHALL name the absolute path and the number of entries written.

All text taken from the session SHALL be HTML-escaped through one helper. Both forms SHALL be interactive-only and SHALL NOT exist in ACP or print mode.

#### Scenario: A JSONL export is byte-identical

- **GIVEN** a session file with three entries
- **WHEN** `/export jsonl copy.jsonl` runs
- **THEN** `copy.jsonl` has exactly the same bytes as the session file

#### Scenario: An HTML export is self-contained

- **GIVEN** a session containing a user message, an assistant answer, a tool call with a diff and a thinking block
- **WHEN** `/export html report.html` runs
- **THEN** the file contains the answer text, the diff lines and the thinking text, references no external URL, and opens with no network

#### Scenario: Escaping survives a hostile tool result

- **GIVEN** a tool result containing `<script>alert(1)</script>`
- **WHEN** the HTML export is written
- **THEN** the script appears as escaped text and the document contains no executable script element

#### Scenario: An existing file is not overwritten silently

- **GIVEN** `report.html` already exists
- **WHEN** `/export html report.html` runs without `--force`
- **THEN** the command refuses with a notice naming the file and leaves it unchanged
