# tui Specification

## Purpose
The interactive foundation: all terminal access behind the `IConsoleIO` seam, raw VT input decoded by the product's own decoder (ADR-0004), and a live area painted from an immutable state snapshot through a merged observable pipeline on an injected scheduler (ADR-0007). Cards T-19 to T-22 build their renderables, tool blocks, bindings and wiring on top of this capability; terminal capability detection and the full platform matrix are T-33's.

## Requirements

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

### Requirement: Markdown subset rendering

Assistant text SHALL be parsed with a CommonMark-compliant parser (Markdig) and rendered by the product's own renderer into Spectre renderables. The subset covers ATX headings 1-6, paragraphs, bullet and ordered lists (nested), blockquotes, fenced code with a language label, and inline bold, italic and code. Every user string SHALL be escaped before markup assembly — bracket text renders literally, never as Spectre markup. Unsupported constructs — tables, links, images, HTML, task lists — SHALL render as readable plain text, never as markup and never throwing. Keyword highlighting covers C#, JSON and shell only, implemented in-product without additional packages.

#### Scenario: Escaping keeps bracket text literal

- **GIVEN** Markdown text containing square-bracket sequences that look like Spectre markup
- **WHEN** it renders
- **THEN** the output shows the original bracket text literally

#### Scenario: Fenced code keeps its language label

- **GIVEN** a fenced code block with a language tag
- **WHEN** it renders
- **THEN** the rendered block shows the language label and highlighted code for C#, JSON or shell; unknown languages render plain

#### Scenario: Unsupported constructs render plain

- **GIVEN** Markdown containing a table, a link, an image, HTML and a task list
- **WHEN** it renders
- **THEN** each renders as readable plain text without Spectre markup and without throwing

#### Scenario: One snapshot per Markdown feature

- **GIVEN** the feature fixtures (headings, emphasis incl. intraword cases, inline code, lists incl. nested, quotes, fences incl. unclosed, highlighting, escaping, unsupported, mixed document)
- **WHEN** they render through the test console
- **THEN** each matches its committed golden byte-for-byte

### Requirement: Culture-invariant technical formatting

Technical readouts SHALL format through one set of helpers — byte sizes, token counts, durations, percentages — invariant by construction under any machine culture, so the de-AT suite pass observes identical output.

#### Scenario: de-AT observes invariant output

- **GIVEN** a byte size, token count, duration and percentage
- **WHEN** the helpers format them under the de-AT culture pass
- **THEN** the strings carry invariant separators and formats (for example `1,540` tokens, `12.5%`)

### Requirement: Tool block rendering

Finished tool calls SHALL render as scrollback blocks carrying the tool name, an argument summary, a status (running, ok, error) and a bounded excerpt of the output — first and last lines with an elision marker for the middle — in a borderless style. Every user-derived string SHALL be escaped before markup assembly, and argument summaries SHALL come from a pure, per-tool summarizer with a generic fallback for unknown tools.

#### Scenario: Long output is elided visibly

- **GIVEN** a tool result whose output exceeds the excerpt budget
- **WHEN** its block renders
- **THEN** the first and last lines are shown and the middle is replaced by an explicit hidden-lines marker

#### Scenario: Status is visible

- **GIVEN** tool results that succeeded, failed and are still running
- **WHEN** their blocks render
- **THEN** each shows its status distinctly (ok green, failed red, running dim)

#### Scenario: Bracket text stays literal

- **GIVEN** arguments or output containing square-bracket sequences that look like Spectre markup
- **WHEN** the block renders
- **THEN** the original text appears literally

### Requirement: Diff rendering with match tier

Edit and write blocks SHALL render their unified diff as a red/green borderless panel — deletions red, insertions green, headers and context dimmed — with the match tier shown as a label, and a normalized (fallback) match visibly flagged so no fallback is silent.

#### Scenario: Both tiers render distinctly

- **GIVEN** an edit that matched exactly and one that matched via normalization
- **WHEN** their blocks render
- **THEN** the diff is red/green in both and the tier label distinguishes exact from normalized (the latter flagged)

#### Scenario: Malformed or empty diffs never break rendering

- **GIVEN** a diff string that is empty or malformed
- **WHEN** the block renders
- **THEN** it renders without the panel (or with the panel omitted) and never throws

### Requirement: One meaning per key

Key events SHALL route to exactly one intent per the guide's table: Enter submits, Alt+Enter and Ctrl+J edit (newline), Esc cancels, Ctrl+C clears or quits, Ctrl+L opens the model picker, Up/Down navigate history, everything else edits. Routing SHALL be pure, and every binding SHALL have a scripted key test.

#### Scenario: Every binding routes distinctly

- **GIVEN** the key events of the guide's binding table
- **WHEN** each is routed
- **THEN** it produces its own intent (and no other binding's), proven by the scripted matrix

#### Scenario: Ctrl+C never cancels a turn

- **GIVEN** a running turn
- **WHEN** Ctrl+C is pressed (any number of times)
- **THEN** the input clears or the quit window arms/quits; no cancel intent exists for it

### Requirement: Ctrl+C quit window

On an empty input, Ctrl+C SHALL arm a quit hint, and a second press within two seconds SHALL quit; with non-empty input it SHALL clear the input and disarm. The window SHALL run on the injected scheduler — virtual time in tests, no wall clock.

#### Scenario: Second press within the window quits

- **GIVEN** an armed window after one Ctrl+C on empty input
- **WHEN** a second Ctrl+C arrives within two seconds of virtual time
- **THEN** the action is quit

#### Scenario: Expired window re-arms

- **GIVEN** an armed window
- **WHEN** more than two seconds of virtual time pass before the next Ctrl+C
- **THEN** that press re-arms instead of quitting

### Requirement: Approval prompt

A pending approval SHALL render as a live-area prompt naming the tool and its argument summary with `[y]es`, `[n]o`, `[a]lways this session`. Only bare `y`, `n` and `a` SHALL decide; a stray Enter SHALL deny (nothing runs by accident); all other keys SHALL not decide. The prompt SHALL produce a choice; session memory for "always" is the wiring layer's concern.

#### Scenario: Explicit keys decide

- **GIVEN** a pending approval
- **WHEN** `y`, `n` or `a` is pressed
- **THEN** the choice is approve, deny or always respectively

#### Scenario: Enter denies

- **GIVEN** a pending approval
- **WHEN** Enter is pressed
- **THEN** the choice is deny

### Requirement: Status footer

The footer SHALL show model, tokens used against the context window with percent, working directory and git branch as one dim line, formatted culture-invariantly. It SHALL degrade deterministically at narrow widths (branch dropped first, then directory; model and usage survive) and omit percent when the window is unknown.

#### Scenario: Narrow width degrades in order

- **GIVEN** a footer too wide for the terminal
- **WHEN** it renders
- **THEN** the branch is dropped first, then the directory, and model and usage remain

#### Scenario: No repository, no branch

- **GIVEN** a working directory outside a git repository
- **WHEN** the footer renders
- **THEN** the branch segment is absent and the rest renders normally

### Requirement: Git branch discovery without processes

The branch SHALL be read from `.git` (directory or worktree `gitdir:` file) without spawning any process; detached HEAD SHALL show the short SHA; any read failure SHALL degrade to no branch.

#### Scenario: Detached HEAD shows a short SHA

- **GIVEN** a repository whose HEAD holds a commit id
- **WHEN** the branch is read
- **THEN** the short SHA (first seven characters) is returned
