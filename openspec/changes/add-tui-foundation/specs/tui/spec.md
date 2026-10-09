## ADDED Requirements

### Requirement: Terminal access through IConsoleIO

All terminal access SHALL go through `IConsoleIO` (read keys, write, size, resize observable), so tests run without a real terminal. The production implementation SHALL enter raw mode through platform APIs — Windows console mode with virtual-terminal input enabled, Unix termios — and SHALL never rely on `Console.ReadKey` for interactive input. When the environment cannot support interactive input (no console attached, redirected handles, `TERM=dumb`), the TUI SHALL fail soft: an actionable diagnostic that names the fix (Windows Terminal or re-enabling the pseudo console; otherwise use print mode) instead of an exception. The `Esc` key's quiet-window and the resize poll SHALL run on the injected scheduler.

#### Scenario: Fake console drives tests without a terminal

- **GIVEN** a scripted `FakeConsoleIO` with queued key presses and a scripted size
- **WHEN** the input line and live area run against it
- **THEN** the decoded keys and every painted frame are observable and no real terminal is touched

#### Scenario: No console fails soft

- **GIVEN** an environment without a console or with `TERM=dumb`
- **WHEN** the support check runs
- **THEN** it returns a one-line actionable diagnostic and does not throw

### Requirement: VT input decoding

Raw input SHALL be decoded by the product VT decoder: control bytes, `Esc`+char as Alt, CSI/SS3 sequences with modifier parameters, tilde-numbered keys (including xterm's non-contiguous numbering), UTF-8 multi-byte characters, and partial sequences buffered until decodable. Bracketed paste SHALL be recognized and delivered as one paste event with markers stripped and `\r\n`/`\r` normalized to `\n`. A lone `Esc` SHALL resolve as the Escape key only after a short quiet window on the injected scheduler.

#### Scenario: Modifier sequence decodes

- **GIVEN** the byte stream for Ctrl+Right
- **WHEN** it is decoded
- **THEN** one key event with Ctrl set and the Right kind is produced

#### Scenario: Bracketed paste is one event

- **GIVEN** a bracketed-paste byte stream containing newlines and special keys as text
- **WHEN** it is decoded
- **THEN** one paste event carries the literal text with normalized newlines and no marker bytes

#### Scenario: Lone escape resolves on the quiet window

- **GIVEN** a lone `Esc` byte and no following bytes
- **WHEN** the quiet window elapses on the test scheduler
- **THEN** the Escape key event is emitted; if a sequence follows instead, no Escape is emitted

### Requirement: Input line editing

The input line SHALL support editing through events: insert (typed characters and pasted text), backspace, delete, cursor movement by character, home/end, and newline insertion. Cursor and paint math SHALL use cell widths (combining marks zero, wide characters two) so non-ASCII text never misplaces the cursor; anything beyond the compact width table is a documented follow-up. History, completion and steering are out of scope until their cards.

#### Scenario: Cursor math with wide characters

- **GIVEN** an input line containing CJK characters
- **WHEN** the cursor moves left or the line renders
- **THEN** columns are computed by cell width, not character count

#### Scenario: Paste inserts literally

- **GIVEN** a paste event containing newlines
- **WHEN** it is applied to the input line
- **THEN** the buffer contains the text unchanged (normalized newlines) and the cursor is after it

### Requirement: Live area threading contract

The live area SHALL be a merged observable pipeline on System.Reactive with an injected `IScheduler`; redraws SHALL be capped by `Sample` at about 30 fps and one render function SHALL paint the live area from an immutable state snapshot through `IConsoleIO` — exactly one writer, state read only after the pipeline has drained through the sample boundary. Time-based behavior (frame cap, spinner, quiet windows) SHALL be tested on `TestScheduler`, never with `Thread.Sleep`. Technical readouts SHALL format culture-invariantly.

#### Scenario: Burst of stimuli paints once per frame

- **GIVEN** several key events within one frame window on the test scheduler
- **WHEN** the frame interval elapses
- **THEN** exactly one frame is painted for that window

#### Scenario: Spinner runs on virtual time

- **GIVEN** an active tool line and TestScheduler time advancing
- **WHEN** spinner intervals elapse
- **THEN** the painted spinner frame advances without any wall-clock wait

#### Scenario: Golden frames

- **GIVEN** a scripted session (typing, paste, resize, spinner ticks)
- **WHEN** the frames recorded by the fake console are compared
- **THEN** they match the committed goldens exactly
