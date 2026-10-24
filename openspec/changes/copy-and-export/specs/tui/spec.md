## Purpose

A copy route that works where the session runs, and an export that produces one safe, self-contained document from a session file, without a second source of truth for either.

## ADDED Requirements

### Requirement: Copy the last answer

`/copy` and `Ctrl+X` SHALL write the final assistant message of the most recent completed turn to the clipboard. When the most recent turn ended in an error or a cancellation, the last completed answer before it SHALL be used; when no turn has completed, the command SHALL report that there is nothing to copy and SHALL write nothing.

The clipboard transport SHALL depend on where the session runs. When `SSH_CONNECTION` or `SSH_TTY` is present, the answer SHALL be written as an OSC 52 sequence and no local clipboard tool SHALL be invoked. Otherwise the platform tool SHALL be tried first, with OSC 52 as the fallback when none exists. On Windows the platform path SHALL be the Win32 clipboard API through P/Invoke and SHALL NOT invoke `clip.exe`, which corrupts non-ASCII text; under WSL `clip.exe` through interop or PowerShell `Set-Clipboard` SHALL be used.

The notice SHALL distinguish evidence from intent: a platform tool SHALL be reported as copied, naming the tool and the character count; OSC 52 SHALL be reported as **sent to the terminal clipboard**, because the write cannot be confirmed. When `TMUX` is set the notice SHALL say once that OSC 52 requires `set-clipboard on`. An answer above 100 KB SHALL be reported as possibly truncated by the terminal, and the text SHALL NOT be truncated by Lunate itself.

#### Scenario: Copy in a local session

- **GIVEN** a completed turn whose answer is 1,842 characters and a session with no ssh variables
- **WHEN** `/copy` runs
- **THEN** the platform clipboard tool receives the exact answer text and the notice names that tool and the character count

#### Scenario: Copy over ssh never touches the local clipboard

- **GIVEN** `SSH_CONNECTION` is set
- **WHEN** `/copy` runs
- **THEN** an OSC 52 sequence carries the answer, no `pbcopy`, `wl-copy`, `xclip` or `clip` process is started, and the notice says the text was sent to the terminal clipboard

#### Scenario: tmux is named once

- **GIVEN** `TMUX` is set and no ssh variables are present
- **WHEN** an OSC 52 copy runs
- **THEN** the notice names `set-clipboard on` as the setting the terminal needs

#### Scenario: Nothing to copy

- **GIVEN** a session where no turn has completed
- **WHEN** `/copy` runs
- **THEN** a notice says there is no completed answer and the clipboard is untouched

#### Scenario: An oversized answer is reported, not cut

- **GIVEN** an answer of 140 KB
- **WHEN** `/copy` runs
- **THEN** the notice says the terminal may truncate the copy and Lunate sends the complete text

### Requirement: Export the session

`/export jsonl [path]` SHALL write the session as JSONL with every entry preserved byte for byte except credential redaction. `/export html [path]` SHALL write one self-contained HTML document built from the session's entries: messages, tool calls with their arguments and results, diffs, thinking blocks, model and turn boundaries, each with its timestamp. Thinking SHALL be rendered in a collapsed `<details>` element by default, and redacted or signature-only thinking SHALL render as `[redacted thinking]`.

Without a path the destination SHALL be `~/.lunate/exports/<yyyy-MM-dd>-<session name or id>.<ext>`, never the current directory. An existing file SHALL be refused unless `--force` is passed. The confirmation SHALL name the absolute path and the number of entries written, and SHALL add one line saying the file may contain file contents and command output and should be reviewed before sharing.

Before writing, every credential value Lunate knows — provider keys from the auth store and environment, extension secrets and MCP environment values — SHALL be replaced with `[redacted:<name>]` in both formats. Replacement SHALL be by exact value only; no heuristic scanning SHALL be performed.

The document SHALL use inline CSS, SHALL contain no JavaScript and no external asset, and SHALL carry `<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; img-src data:">`. Every value taken from the session SHALL be written as escaped text and never as markup built from content. Both forms SHALL be interactive-only and SHALL NOT exist in ACP or print mode.

#### Scenario: A JSONL export preserves every entry

- **GIVEN** a session file with three entries and one known provider key
- **WHEN** `/export jsonl` runs
- **THEN** every entry is byte-identical except the key, which is `[redacted:<name>]`

#### Scenario: The default destination is the exports directory

- **GIVEN** a session named `parser-refactor` on 2026-10-24
- **WHEN** `/export html` runs without a path
- **THEN** the file is written to `~/.lunate/exports/2026-10-24-parser-refactor.html` and the absolute path is printed

#### Scenario: An HTML export is self-contained

- **GIVEN** a session containing a user message, an assistant answer, a tool call with a diff and a thinking block
- **WHEN** `/export html` runs
- **THEN** the file contains the answer, the diff lines and the thinking text in a collapsed `<details>`, references no external URL, carries the CSP meta tag, and contains no script element

#### Scenario: Hostile tool output stays inert

- **GIVEN** a tool result containing `<script>alert(1)</script>` and an `<img onerror=...>` attribute
- **WHEN** the HTML export is written
- **THEN** both appear as escaped text, the document has no executable script element and no event-handler attribute

#### Scenario: A known credential never reaches the file

- **GIVEN** a session whose tool output contains a provider key Lunate holds
- **WHEN** either export form is written
- **THEN** the key's exact value appears nowhere in the file and `[redacted:<name>]` stands in its place

#### Scenario: An existing file is not overwritten silently

- **GIVEN** `report.html` already exists at the destination
- **WHEN** `/export html report.html` runs without `--force`
- **THEN** the command refuses with a notice naming the file and leaves it unchanged
