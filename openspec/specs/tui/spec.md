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

The input line SHALL support editing through events: insert (typed characters and pasted text), backspace, delete, cursor movement by character, word movement with Alt+B and Alt+F, line start and end with Ctrl+A and Ctrl+E, home/end, and newline insertion. Ctrl+K and Ctrl+U SHALL kill from the cursor to the end and to the start of the line, Ctrl+W and Alt+D SHALL kill the word before and after the cursor, Ctrl+Y SHALL insert the most recent kill, and Ctrl+_ SHALL undo the last edit, restoring the exact buffer and cursor. Undo history SHALL be bounded at 64 entries and cleared by submit and by newline insertion. Cursor and paint math SHALL use cell widths (combining marks zero, wide characters two) so non-ASCII text never misplaces the cursor; anything beyond the compact width table is a documented follow-up. Word boundaries SHALL be computed on cell text, never splitting a surrogate pair.

#### Scenario: Cursor math with wide characters

- **GIVEN** an input line containing CJK characters
- **WHEN** the cursor moves left or the line renders
- **THEN** columns are computed by cell width, not character count

#### Scenario: Paste inserts literally

- **GIVEN** a paste event containing newlines
- **WHEN** it is applied to the input line
- **THEN** the buffer contains the text unchanged (normalized newlines) and the cursor is after it

#### Scenario: Kill and yank round-trip

- **GIVEN** the input `git commit -m "x"` with the cursor after `git`
- **WHEN** Ctrl+K is pressed and then Ctrl+Y
- **THEN** the buffer is unchanged from before the kill and the cursor is at the end

#### Scenario: Undo restores buffer and cursor

- **GIVEN** an edit that inserted and then deleted text
- **WHEN** Ctrl+_ is pressed
- **THEN** the buffer and the cursor are exactly as before the last edit

#### Scenario: Word movement respects wide characters

- **GIVEN** a line mixing CJK and Latin words
- **WHEN** Alt+F moves forward
- **THEN** the cursor lands on the next word start without splitting a surrogate pair

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

Finished tool calls SHALL render as scrollback blocks carrying the tool name, an argument summary, a status (running, ok, error) and a bounded excerpt of the output — first and last lines with an elision marker for the middle — in a borderless style. Each header SHALL lead with a status glyph — `✓` ok, `✗` failed (ASCII `+` and `x`) — and SHALL carry a per-tool compact summary with meta: `read` shows its path with the line range and no output, `bash` its command with the exit code and duration and an excerpt of the output's last lines, `edit` and `write` their path with `+N −M` change counts followed by the diff, `cs_*` tools their severity counts with the top items, and extension and MCP tools their name with a short argument summary. Below 60 columns the header SHALL shorten to glyph, name and meta only — `read` carries its line count as the meta, `bash` its exit code and duration, `edit` and `write` their change counts, `cs_*` its severity counts, and the extension/MCP fallback its name alone. Paths in headers and excerpts SHALL be shortened in the middle, never wrapped. The elision marker SHALL carry the hidden-line count and the `ctrl+o` affordance in the warning colour. Every user-derived string SHALL be escaped before markup assembly, and argument summaries SHALL come from a pure, per-tool summarizer with a generic fallback for unknown tools. Child-run (subagent) events SHALL NOT render as scrollback text or blocks of their own; while a child run is live the live area SHALL show a `subagent running` status, the spawning tool's own block carries the child's final result, and child usage still feeds the footer — the child's transcript is inspectable through its child session. The spawning block's carried child result SHALL render as one indented group. A successful image `read` SHALL render its `[image <width>x<height>]` output line even though a text `read` hides output; the placeholder SHALL NOT count as a content line in the header, and no block SHALL emit a terminal image escape sequence.

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

#### Scenario: A child run stays quiet in the transcript

- **GIVEN** a tool call running a subagent that streams text and tool calls
- **WHEN** its child events arrive on the parent stream
- **THEN** no child text or child tool block appears in scrollback, the live area shows `subagent running` while it is live, and the spawning block carries the child's final result
#### Scenario: Per-tool compact forms

- **GIVEN** `read`, `bash`, `edit` and `cs_*` results
- **WHEN** their blocks render
- **THEN** each header carries its compact summary and meta — `read` the path with its line range
  and no content, `bash` the command with its exit code and duration and the output's last lines,
  `edit` the path with `+N −M` and the diff, `cs_*` the severity counts with the top items

#### Scenario: The cut is an affordance

- **GIVEN** a tool result whose output exceeds the excerpt budget
- **WHEN** its block renders
- **THEN** the elision marker names the hidden-line count and the `ctrl+o` affordance

#### Scenario: Paths shorten in the middle

- **GIVEN** a header or excerpt carrying a path longer than its budget
- **WHEN** the block renders
- **THEN** the path shortens in the middle with its basename kept and never wraps

#### Scenario: Headers shorten below 60 columns

- **GIVEN** a 50-column terminal and `read`, `bash`, `edit` and `cs_*` results
- **WHEN** their blocks render
- **THEN** each header is glyph, name and meta only (`✓ read · 13 lines`, `✓ bash · exit 0 · 0.4s`, `✓ edit · +5 −1`) with no summary

#### Scenario: Collapsed is the default and shows only the header

- **GIVEN** a finished `bash` result with output and an `edit` result with a diff
- **WHEN** their blocks commit with the default `toolDetails`
- **THEN** each block is exactly its header line, with no excerpt and no diff, and each header ends with `ctrl+o`

#### Scenario: The hint appears only when more exists

- **GIVEN** a successful `read` with output, a `bash` with output, a `bash` with blank output and an `edit` with an empty diff
- **WHEN** their blocks commit collapsed
- **THEN** only the `bash` with output and the `edit` with a diff carry `ctrl+o`; the successful `read`, the blank `bash` and the empty diff do not

#### Scenario: Expanded mode restores the details

- **GIVEN** `toolDetails: expanded`, a `bash` result with output and an `edit` result with a diff
- **WHEN** their blocks commit
- **THEN** the header is followed by the bounded excerpt and the diff panel, and no header carries `ctrl+o`

### Requirement: Diff rendering with match tier

In expanded mode (`toolDetails: expanded`): Edit and write blocks SHALL render their unified diff as a red/green borderless panel — deletions red, insertions green, headers and context dimmed — with the match tier shown as a label, and a fallback match (`normalized` or `indent`) visibly flagged so no fallback is silent. By default (`toolDetails: collapsed`), an edit or write block SHALL commit its header line only and SHALL NOT render the diff panel; when the diff parses into a panel, the header SHALL carry the `ctrl+o` hint, and an empty or malformed diff SHALL add no hint and SHALL not throw. The transcript viewer SHALL keep showing the full diff in both modes.

#### Scenario: Both tiers render distinctly

- **GIVEN** an edit that matched exactly and one that matched via normalization, with `toolDetails: expanded`
- **WHEN** their blocks render
- **THEN** the diff is red/green in both and the tier label distinguishes exact from normalized (the latter flagged)

#### Scenario: Malformed or empty diffs never break rendering

- **GIVEN** a diff string that is empty or malformed, with `toolDetails: expanded`
- **WHEN** the block renders
- **THEN** it renders without the panel (or with the panel omitted) and never throws

#### Scenario: An indent fallback renders flagged

- **GIVEN** an edit that matched via the indent tier, with `toolDetails: expanded`
- **WHEN** its block renders
- **THEN** the tier label says indent and the fallback is flagged like normalized

#### Scenario: Collapsed hides the diff behind the hint

- **GIVEN** an `edit` result with a non-empty diff
- **WHEN** its block commits with the default `toolDetails`
- **THEN** only the header line appears, the diff and tier label are absent, and the header ends with `ctrl+o`

#### Scenario: Collapsed malformed diff adds no hint

- **GIVEN** an `edit` result with a malformed diff
- **WHEN** its block commits collapsed
- **THEN** only the header appears and it carries no `ctrl+o` hint

#### Scenario: Expanded renders the diff panel

- **GIVEN** `toolDetails: expanded` and an `edit` result with a diff
- **WHEN** its block commits
- **THEN** the red/green panel and the tier label follow the header and the header carries no `ctrl+o`

#### Scenario: A read image shows the placeholder

- **GIVEN** a successful `read` of a 1024×768 PNG
- **WHEN** its block renders
- **THEN** the output line reads `[image 1024x768]`, the header does not claim a line count, and the block contains no terminal image escape

#### Scenario: The image block golden

- **GIVEN** the image read block at 120, 80 and 50 columns
- **WHEN** it renders through the test console
- **THEN** each matches its committed golden byte for byte

### Requirement: Ctrl+C quit window

On an empty input with no turn running, Ctrl+C SHALL arm a quit hint, and a second press within two seconds SHALL quit; with non-empty input it SHALL clear the input and disarm. While a turn runs, Ctrl+C SHALL interrupt the turn and SHALL NOT arm the window. Ctrl+D SHALL quit on a second press within 800 milliseconds, armed only when the input is empty and no turn runs; with text present or a turn running it SHALL NOT arm. Both windows SHALL run on the injected scheduler — virtual time in tests, no wall clock.

#### Scenario: Second press within the window quits

- **GIVEN** an armed window after one Ctrl+C on an empty input
- **WHEN** a second Ctrl+C arrives within two seconds of virtual time
- **THEN** the action is quit

#### Scenario: Expired window re-arms

- **GIVEN** an armed window
- **WHEN** more than two seconds of virtual time pass before the next Ctrl+C
- **THEN** that press re-arms instead of quitting

#### Scenario: Ctrl+C interrupts instead of arming

- **GIVEN** a running turn and an empty input
- **WHEN** Ctrl+C is pressed twice within two seconds
- **THEN** the turn is cancelled once and the session does not quit

#### Scenario: Ctrl+D quits on the second press

- **GIVEN** an idle, empty prompt
- **WHEN** Ctrl+D is pressed twice within 800 milliseconds of virtual time
- **THEN** the action is quit

#### Scenario: Ctrl+D does not arm mid-turn or with text

- **GIVEN** a running turn, and separately an idle prompt holding text
- **WHEN** Ctrl+D is pressed twice in either state
- **THEN** no quit occurs and the input is unchanged

### Requirement: Approval prompt

A pending approval SHALL render as a live-area prompt naming the tool and its argument summary with `[y]es`, `[n]o`, `[a]lways this session`. The prompt SHALL be a panel: a warning headline with the concrete action (the command for `bash`, the path with its change counts for `edit` and `write`), a bounded detail block (the command's lines, the edit's diff, or the tool's key arguments, clipped never wrapped), and the question with its choices; below 60 columns the detail shortens first and every choice SHALL stay visible, never clipped mid-option. Bare `y`, `n` and `a` SHALL decide directly. Up and Down SHALL move a selection across the three choices, rendering it without clipping any choice, and Space SHALL activate the selected choice. Escape SHALL deny. A stray Enter SHALL deny (nothing runs by accident); all other keys SHALL not decide. The prompt SHALL produce a choice; session memory for "always" is the wiring layer's concern and SHALL apply across the whole run tree — a grant given while a subagent asked covers every run of the session. When the asking run is a subagent, the prompt SHALL label it (its depth relative to the top-level run).

#### Scenario: Explicit keys decide

- **GIVEN** a pending approval
- **WHEN** `y`, `n` or `a` is pressed
- **THEN** the choice is approve, deny or always respectively

#### Scenario: Enter denies

- **GIVEN** a pending approval
- **WHEN** Enter is pressed
- **THEN** the choice is deny

#### Scenario: Selection activates with Space

- **GIVEN** a pending approval
- **WHEN** Down is pressed twice and Space once
- **THEN** the selection has moved to `[a]lways this session` and the choice is always

#### Scenario: Escape denies

- **GIVEN** a pending approval
- **WHEN** Escape is pressed
- **THEN** the choice is deny

#### Scenario: A subagent's request is labelled

- **GIVEN** a tool call asked by a child (subagent) run
- **WHEN** its approval prompt renders
- **THEN** the prompt names the asking run as a subagent (its depth), and pressing `a` covers that tool for the whole tree afterwards

#### Scenario: The detail is visible before deciding

- **GIVEN** a pending `bash` approval and a pending `edit` approval
- **WHEN** their panels render
- **THEN** the bash panel shows the command's lines and the edit panel shows the path, the change counts and the diff, each bounded with a marker when cut

#### Scenario: Every choice survives 40 columns

- **GIVEN** a pending approval in a 40-column terminal
- **WHEN** the panel renders
- **THEN** `[y]es`, `[n]o` and `[a]lways this session` are all fully visible on their own line and the detail is shortened, not the choices

#### Scenario: The detail never pushes the question away

- **GIVEN** a pending approval whose detail exceeds the terminal height
- **WHEN** the live frame renders
- **THEN** the detail is trimmed first and the question line always renders

### Requirement: Status footer

The footer SHALL show model, tokens used against the context window with percent, working directory and git branch as one dim line, formatted culture-invariantly. When the thinking level is not `off`, a `think:<level>` segment SHALL appear after the usage segment. When the session's model is a virtual or extension-registered model, the model segment SHALL show the last routed resolution (`auto → deep`), updating as `ModelRouted` events arrive. When the active model is in the favourites list, a `fav:<position>/<count>` segment SHALL appear after the usage segment. When every token lane of the current turn is priced, a cost segment SHALL appear after the favourites segment; an unpriced turn SHALL omit it rather than show zero. Extension status widgets, when registered, SHALL append their segments after the core fields in registration order. It SHALL degrade deterministically at narrow widths (widget segments drop first, then branch, then directory, then thinking, then favourites, then cost; model and usage survive) and omit percent when the window is unknown. Below 60 columns the footer SHALL switch to the compact form `model · <pct>%`, dropping the token counts along with the favourites, cost, thinking, directory, branch and widget segments regardless of remaining width. The interactive session SHALL provide a `/context` command showing the context usage plus the resolved subagent limits (concurrency, per-child and per-tree token budgets, the fixed depth) with the current tree usage.

#### Scenario: Narrow width degrades in order

- **GIVEN** a footer too wide for the terminal
- **WHEN** it renders
- **THEN** widgets drop first, then the branch, then the directory, then thinking, then favourites, then cost, and model and usage remain

#### Scenario: No repository, no branch

- **GIVEN** a working directory outside a git repository
- **WHEN** the footer renders
- **THEN** the branch segment is absent and the rest renders normally

#### Scenario: Widget segments append after the core fields

- **GIVEN** a registered status widget returning a segment
- **WHEN** the footer renders
- **THEN** the segment appears after model, usage, favourites and cost, and drops before them at narrow widths

#### Scenario: /context lists the subagent limits

- **GIVEN** an interactive session after a subagent ran
- **WHEN** `/context` is entered
- **THEN** the context usage and the resolved subagent limits with the current tree usage are shown

#### Scenario: The footer shows the last route

- **GIVEN** a session on a virtual model
- **WHEN** a call routes to the `deep` backend
- **THEN** the footer's model segment shows `auto → deep`

#### Scenario: The footer shows the thinking level

- **GIVEN** a session with thinking level `medium`
- **WHEN** the footer renders
- **THEN** the segment `think:medium` appears after the usage segment, and at level `off` it is absent

#### Scenario: The compact footer below 60 columns

- **GIVEN** a terminal 40 columns wide with tokens and a context window known
- **WHEN** the footer renders
- **THEN** it reads `model · <pct>%` with no tokens, window, favourites, cost, thinking, directory, branch or widget segments

#### Scenario: An unknown window omits the percent at 40 columns

- **GIVEN** a 40-column terminal whose context window is unknown
- **WHEN** the footer renders
- **THEN** only the model segment appears

#### Scenario: The footer shows the favourite position

- **GIVEN** a favourites list of five with the active model at position two
- **WHEN** the footer renders
- **THEN** it carries `fav:2/5` after the usage segment

#### Scenario: An unpriced turn has no cost segment

- **GIVEN** a turn whose model has no catalog pricing
- **WHEN** the footer renders
- **THEN** the token usage is unchanged and no cost segment appears

### Requirement: Git branch discovery without processes

The branch SHALL be read from `.git` (directory or worktree `gitdir:` file) without spawning any process; detached HEAD SHALL show the short SHA; any read failure SHALL degrade to no branch.

#### Scenario: Detached HEAD shows a short SHA

- **GIVEN** a repository whose HEAD holds a commit id
- **WHEN** the branch is read
- **THEN** the short SHA (first seven characters) is returned

### Requirement: Input pipeline

Every submitted text — a new turn or a steering message — SHALL pass through the `InputReceived` hook chain before anything starts or queues: a transformed text is what proceeds, a consumed input starts and queues nothing and is reported as a notice. With no hook runner configured, input passes through unchanged.

#### Scenario: A consuming handler stops the input

- **GIVEN** an installed handler that consumes input
- **WHEN** the user submits text
- **THEN** no run starts and nothing is queued, and a notice is shown

### Requirement: Interactive session wiring

The interactive session SHALL consume the harness's event stream: streaming text renders as a live tail whose completed paragraphs commit to scrollback as Markdown; tool results commit as tool blocks; usage updates the footer; retrying, compaction and step-limit appear as notices. The submitted user message SHALL commit to the scrollback as the first block of its turn — `> ` prefix in the theme's `user` colour, the wrapped continuation indented two spaces, one blank line after — carrying the same processed text as the transcript entry, and steering SHALL keep its distinct dim echo. The approval prompt SHALL appear in the live area and block new input until decided; `always` memory SHALL be session-scoped per tool name. Input history SHALL live in `~/.lunate/history` and be navigable with Up/Down. The live area and the key pump SHALL run while the session composes, so session-start questions are answered through the normal key path; until composition completes, ordinary input stays inert (only prompt keys and the quit window act) and the footer SHALL show a `loading project…` status (ASCII fallback `loading project...`).

#### Scenario: Streaming commits completed paragraphs

- **GIVEN** a run whose text output spans several paragraphs
- **WHEN** paragraphs complete during streaming
- **THEN** completed paragraphs are rendered as Markdown in scrollback and only the unfinished tail stays live

#### Scenario: Approval blocks steering

- **GIVEN** an open approval prompt
- **WHEN** the user types
- **THEN** the input line accepts no new text; only prompt keys and the quit window act

#### Scenario: Input stays inert during composition

- **GIVEN** a session still composing, for example with a trust prompt open
- **WHEN** the user types ordinary text
- **THEN** the input line accepts no text and submitting is inert; only prompt keys and the quit window act

#### Scenario: Loading status during composition

- **GIVEN** a session that is still composing
- **WHEN** the footer renders
- **THEN** it shows the loading status until the real status replaces it

#### Scenario: The submitted message opens the turn

- **GIVEN** a session at the prompt
- **WHEN** the user submits a message and the run starts
- **THEN** the scrollback gains the `> `-prefixed user block in the user colour before any assistant output, and the transcript entry carries the same text

#### Scenario: A wrapped message keeps the hanging indent

- **GIVEN** a submitted message longer than the terminal width
- **WHEN** its block commits
- **THEN** the continuation lines are indented two spaces and the block ends with one blank line

#### Scenario: The echo is legible without colour

- **GIVEN** NO_COLOR or the 16-color mode
- **WHEN** the user block renders
- **THEN** the `> ` prefix alone distinguishes it from assistant text

### Requirement: Steering interaction semantics

`Esc` SHALL cancel the running turn and return any unsent queued steering to the input line. A normally finished run with leftover steering SHALL auto-start the next run with that text; a run that ended in error or step limit SHALL not auto-run and SHALL return the leftover to the input line.

#### Scenario: Esc returns queued steering to the input

- **GIVEN** a running turn with a queued steering message
- **WHEN** the user presses Esc
- **THEN** the turn cancels and the queued text appears in the input line, unsent

#### Scenario: Normal finish auto-runs leftover steering

- **GIVEN** a run that finishes normally with leftover queued steering
- **WHEN** the run ends
- **THEN** the next run starts automatically with the leftover text

#### Scenario: Error does not auto-run

- **GIVEN** a run that ends in an error or step limit with leftover queued steering
- **WHEN** the run ends
- **THEN** no run starts and the leftover text returns to the input line

### Requirement: Slash commands

A submitted line whose first non-space character is `/` SHALL dispatch as a client command and never reach the model: `/model` opens the model picker (`/model <id>` switches directly when the id is in the catalog), and when the current model is an extension-registered or virtual model the picker's current-model line and `/model` SHALL show its last routed resolution (`auto → deep`); `/new` starts a fresh session, `/name <title>` names the current session and bare `/name` reports the stored name, `/session` reports the current session's path, id, message count and token totals, `/resume` opens the session picker, `/compact` requests an explicit out-of-run compaction, `/thinking <level>` sets the session's reasoning level (one of `off`/`low`/`medium`/`high`; `/thinking` alone prints the current level and the active model's mapping), `/mcp` lists the configured and extension-registered MCP servers with their status and source (the config file path it came from, or `extension '{id}'`), with a name collision shown as the configured server winning and the registration marked shadowed, a server whose definition references an unset environment variable without a default shown with that missing-variable warning next to it, and never printing environment values or headers, `/extensions` lists every discovered extension — failed first, then trust-denied, then loaded, each group by id — as one segment per extension: `id version (scope) state`, with loaded ones carrying their load time and registered tool, command, hook and service counts (a field differing from the manifest reads `(N declared)`, a load above 500 ms reads `(slow)`), trust-denied ones reading `trust-denied` with the manifest's declared counts, failed ones reading `failed` followed by the message's first sentence and ` — see the log` — reading `extensions: none configured` when there are none and `extensions disabled (--no-extensions)` when extensions are disabled, `/trust` lists this repository's trust records (`/trust clear` clears them, so the next load asks again), `/skills` lists the discovered skills with their scope and state, `/skill:<name> [args]` invokes the named skill by submitting its wrapped body as a user turn through the normal submission path — an unknown name warns instead — `/help` lists the commands grouped (session / model / extensions / files / quit) with one-line descriptions taken from the command registry — no hand-maintained list: a command dispatchable without a description SHALL fail a test — `/quit` quits. Commands SHALL append to the input history and skip the input pipeline; an unknown command SHALL produce a dim notice naming it. While a turn is running, `/new`, `/resume`, `/model` and `/compact` SHALL be refused with a notice; `/quit` SHALL quit at any time (cancelling a running turn). While an approval prompt or a picker is open, no input is submitted, so no command can dispatch.

#### Scenario: Every built-in command dispatches without a model call

- **GIVEN** an idle scripted session
- **WHEN** each built-in command is submitted
- **THEN** it takes its action, the request count is unchanged, and the command text is recalled by Up

#### Scenario: Unknown command

- **GIVEN** an idle session
- **WHEN** `/nope` is submitted
- **THEN** a dim notice names the command, no run starts, and nothing reaches the model

#### Scenario: Busy session refusals

- **GIVEN** a running turn
- **WHEN** `/new`, `/resume`, `/model` or `/compact` is submitted
- **THEN** a notice refuses it and the run is untouched

#### Scenario: /compact without a compactable tail

- **GIVEN** an idle session whose history has nothing older than the kept tail
- **WHEN** `/compact` is submitted
- **THEN** a notice says nothing was compacted

#### Scenario: Commands skip the input pipeline

- **GIVEN** an installed consuming input hook
- **WHEN** a command is submitted
- **THEN** the hook never sees the command text and the command still acts

#### Scenario: /mcp lists configured servers with status

- **GIVEN** an idle session with one started and one failing configured MCP server
- **WHEN** `/mcp` is submitted
- **THEN** a notice lists both servers with their status (started with its tool count; failed with the reason), without a model call

#### Scenario: /mcp lists extension-registered servers with status and source

- **GIVEN** an idle session where an extension registered an MCP server and no configured servers exist
- **WHEN** `/mcp` is submitted
- **THEN** a notice lists the extension-registered server with its status and `extension '{id}'` as its source, without a model call

#### Scenario: /mcp shows a name collision with the configured server winning

- **GIVEN** an idle session with a configured server and an extension registration sharing its name
- **WHEN** `/mcp` is submitted
- **THEN** the notice shows the configured server with its status and source, and the registration as shadowed by the configured source, without a model call

#### Scenario: /mcp shows a crashed server as failed

- **GIVEN** a session whose started MCP server process has exited
- **WHEN** `/mcp` is submitted
- **THEN** the notice shows the server as failed with `the server process exited`, and a notice named the server

#### Scenario: /mcp never prints environment values or headers

- **GIVEN** an idle session whose servers carry environment values
- **WHEN** `/mcp` is submitted
- **THEN** the notice contains neither the environment values nor any header names

#### Scenario: /trust lists and clears this repository's records

- **GIVEN** a repository with recorded trust decisions
- **WHEN** `/trust` is submitted
- **THEN** a notice lists them, and after `/trust clear` the records are gone and a later load asks again

#### Scenario: The picker shows routing for a virtual model

- **GIVEN** a session whose model is a virtual model with a recorded last resolution
- **WHEN** the model picker opens
- **THEN** its current-model line shows `auto → deep`

#### Scenario: /mcp shows a missing-variable warning next to the server

- **GIVEN** an idle session whose server definition references an unset `${VAR}` with no default
- **WHEN** `/mcp` is submitted
- **THEN** the server's entry carries the missing-variable warning, and no model call happens

#### Scenario: /extensions lists the discovered extensions with scope, state and contributions

- **GIVEN** an idle session with one loaded global extension and one project extension whose trust was denied
- **WHEN** `/extensions` is submitted
- **THEN** one segment per extension appears ordered failed, trust-denied, loaded — the loaded one showing `(global) loaded in N ms` and its registered counts, the denied one showing `(project) trust-denied` and its declared counts — and no model call happens

#### Scenario: /extensions marks a slow load and truncates a failed message

- **GIVEN** an idle session with a loaded extension whose load exceeded the threshold and a failed extension with a multi-sentence message
- **WHEN** `/extensions` is submitted
- **THEN** the loaded segment carries `(slow)` and the failed segment carries the message's first sentence followed by ` — see the log`

#### Scenario: /extensions with no extensions discovered or disabled

- **GIVEN** an idle session with no extensions discovered (or one started with `--no-extensions`)
- **WHEN** `/extensions` is submitted
- **THEN** the notice reads `extensions: none configured` (or `extensions disabled (--no-extensions)`), and no model call happens

#### Scenario: /skills lists discovered skills with scope and state

- **GIVEN** an idle trusted session with one project skill and one `disable-model-invocation` user skill
- **WHEN** `/skills` is submitted
- **THEN** a notice lists both with their scopes and the command-only marker, and no model call happens

#### Scenario: /skill:name invokes the skill

- **GIVEN** an idle trusted session with skill `greek` discovered
- **WHEN** `/skill:greek` is submitted
- **THEN** the skill body reaches the model as a wrapped user turn and exactly one run starts

#### Scenario: /skill:unknown warns without a run

- **GIVEN** an idle session without a skill named `ghost`
- **WHEN** `/skill:ghost` is submitted
- **THEN** a dim notice reads `unknown skill: ghost (see /skills)` and no run starts

#### Scenario: /help lists every command with a description

- **GIVEN** an idle session
- **WHEN** `/help` is submitted
- **THEN** the listing shows every built-in command grouped (session / model / extensions / files / quit), each with a one-line description, and no command appears without one

#### Scenario: /thinking sets and reports the level

- **GIVEN** an idle session
- **WHEN** `/thinking high` then `/thinking` are submitted
- **THEN** the level is set to `high`, the footer shows `think:high`, and the bare command prints the current level with the active model's mapping — without a model call

### Requirement: SelectList pickers

A picker SHALL render as a select list in the live area — a title and one line per item (a bounded window keeps the selection visible), the selected item marked and emphasized — navigated with Up/Down (clamped at the ends), confirmed with Enter, dismissed with Esc; every other key SHALL be ignored while a picker is open. Pickers SHALL open only while idle. The model picker (Ctrl+L or `/model`) SHALL list the catalog's models, mark the current one, and on selection record a model change in the session and continue the conversation on the new model; the footer SHALL show the new model. The session picker (`/resume`) SHALL list the session directory's sessions, newest first, and on selection resume the chosen session — its history becomes the conversation and a notice names the resumed session. The picker render SHALL be pinned by a golden.

#### Scenario: Choosing a model switches without losing the conversation

- **GIVEN** an idle session with a completed turn
- **WHEN** the model picker is opened and another catalog model is chosen
- **THEN** the session gains a model-change entry, the footer names the new model, and the next request continues the same history

#### Scenario: Esc dismisses without switching

- **GIVEN** an open picker
- **WHEN** Esc is pressed
- **THEN** the picker closes and nothing changed

#### Scenario: Resume restores the conversation

- **GIVEN** two sessions in the session directory
- **WHEN** the picker resumes the older one
- **THEN** the next request carries that session's history and a notice names it

#### Scenario: Picker keys do not reach the input line

- **GIVEN** an open picker
- **WHEN** Up, Down, characters and Enter are pressed
- **THEN** only the selection changes; no text is edited and no run starts

### Requirement: Steering echo

When the harness injects a steering message (`SteeringInjected`), the session SHALL commit a dim echo block — `» <text>`, or `>> <text>` when Unicode support is off — to scrollback, in injection order; steering returned to the input line (Esc, error, step limit) SHALL never be echoed.

#### Scenario: Injected steering is echoed

- **GIVEN** a running turn with a queued steering message
- **WHEN** the message is injected before the next model call
- **THEN** the scrollback gains the dim echo, in order

#### Scenario: Returned leftovers are not echoed

- **GIVEN** queued steering that the user takes back with Esc
- **WHEN** the run ends
- **THEN** no echo for it appears in scrollback

### Requirement: Interactive entry

Bare `lunate` (no arguments) SHALL start interactive mode when the console is a terminal; when it is not (or `TERM=dumb`), it SHALL exit non-zero with a hint to use `lunate -p`. Interactive mode SHALL read keys through the real console and schedule on the default scheduler; `--help` SHALL document the entry and list the commands.

#### Scenario: Not a terminal

- **GIVEN** a console that is not interactive
- **WHEN** `lunate` runs
- **THEN** it exits with a non-zero code and the hint names `lunate -p`

#### Scenario: A terminal starts the session

- **GIVEN** a scripted terminal (test seam)
- **WHEN** `lunate` runs
- **THEN** the interactive session starts and ends with the console

### Requirement: Terminal capability detection and fallbacks

The TUI SHALL detect terminal capabilities once per session and degrade every capability to a tested fallback. Colour depth SHALL come from `NO_COLOR` (set and non-empty disables colour everywhere), `COLORTERM` (containing `truecolor` or `24bit`, case-insensitive → true colour) and Spectre's detection, with non-interactive output always colourless. Unicode support SHALL come from `TERM=dumb` (off), Windows (`WT_SESSION` set, or the console output code page 65001) and Unix locales (UTF-8 in the effective `LC_ALL`/`LC_CTYPE`/`LANG`; on when unset), overridable via `LUNATE_ASCII` (force off; wins when both overrides are set) and `LUNATE_UNICODE` (force on). Without Unicode the renderer SHALL use ASCII stand-ins for every decorative glyph — markdown bullets `• ` → `- `, blockquote prefixes `│ ` → `| `, the steering echo prefix `» ` → `>> ` — and the spinner stays ASCII by design. Detection SHALL take injectable environment and code-page seams so every branch is covered by tests.

#### Scenario: NO_COLOR disables colour

- **GIVEN** `NO_COLOR` set and non-empty
- **WHEN** capabilities are detected
- **THEN** the colour system is colourless even when `COLORTERM` claims true colour

#### Scenario: COLORTERM upgrades the colour depth

- **GIVEN** `NO_COLOR` unset and `COLORTERM=truecolor`
- **WHEN** capabilities are detected
- **THEN** the colour system is true colour

#### Scenario: Non-interactive output is always colourless

- **GIVEN** a non-interactive console
- **WHEN** capabilities are detected
- **THEN** the colour system is colourless regardless of the environment

#### Scenario: Unicode is detected per platform

- **GIVEN** `TERM=dumb`, or Windows without `WT_SESSION` and a non-UTF-8 code page, or a Unix `C` locale
- **WHEN** capabilities are detected
- **THEN** Unicode support is off; with `WT_SESSION`, code page 65001, a UTF-8 locale, or no locale set it is on

#### Scenario: The overrides win

- **GIVEN** `LUNATE_ASCII` or `LUNATE_UNICODE` set
- **WHEN** capabilities are detected
- **THEN** the override decides, and `LUNATE_ASCII` wins when both are set

#### Scenario: ASCII mode replaces every decorative glyph

- **GIVEN** Unicode support off and a document with bullets and a blockquote
- **WHEN** it renders, and when a steering message is echoed
- **THEN** the bullets render as `- `, the blockquote as `| `, the echo as `>> `, and the output matches the committed golden

### Requirement: Trust prompt

A pending trust decision (a project extension, a project `mcp.json` file, or a per-server approval) SHALL render as a live-area prompt naming what is trusted — for an extension its id, version, scope and directory (saying so when it re-prompts because files changed); for a config file its path, its server names and their truncated commands with a count; for a per-server approval the server's name, its source file and its full command, untruncated — with `[y]es` `[n]o`. Only a bare `y` or `Y` SHALL approve; `n`, `Enter` and `Esc` SHALL deny (nothing loads by accident); Ctrl+C SHALL neither approve nor deny (the quit window keeps its behavior); all other keys SHALL not decide. Keys received before the question is shown SHALL never answer it. While it is open the input line SHALL accept no text, like an approval. When several decisions are pending they SHALL be asked one at a time, labeled `n of m`. It SHALL be answered through the normal key path — the session's key pump runs while the session composes — never through a raw stdin line read.

#### Scenario: y or Y approves

- **GIVEN** a pending trust prompt
- **WHEN** `y` or `Y` is pressed
- **THEN** the decision is approve

#### Scenario: n, Enter and Esc deny

- **GIVEN** a pending trust prompt
- **WHEN** `n`, Enter or Esc is pressed
- **THEN** the decision is deny

#### Scenario: Ctrl+C does not decide

- **GIVEN** a pending trust prompt
- **WHEN** Ctrl+C is pressed
- **THEN** the quit window acts and the prompt stays undecided

#### Scenario: Type-ahead never answers

- **GIVEN** keys typed while the session is still loading, before the question is shown
- **WHEN** the trust question appears
- **THEN** those keys never answer it and the prompt stays pending

#### Scenario: Other keys do not decide

- **GIVEN** a pending trust prompt
- **WHEN** any other key is pressed
- **THEN** the prompt stays pending and no text reaches the input line

#### Scenario: Several decisions are asked one at a time

- **GIVEN** two pending trust decisions at session start
- **WHEN** the questions are asked
- **THEN** the first shows `1 of 2` and is answered before the second shows `2 of 2`

#### Scenario: The prompt is answered while the session composes

- **GIVEN** an untrusted project extension and an interactive session starting
- **WHEN** the trust prompt appears during composition
- **THEN** keys answer it through the same key path the running session uses

#### Scenario: A per-server approval shows the full command

- **GIVEN** a pending per-server approval whose command is longer than the config-file prompt's truncation
- **WHEN** the prompt appears
- **THEN** it names the server, its source file and the full untruncated command, and `y` approves it

### Requirement: Welcome header

An interactive session SHALL print a welcome header once per session start, on the first frame, as finished Spectre output into scrollback — never as a splash: no animation, no blocking, no alternate screen, and the composition (extensions, MCP, skills) SHALL keep running underneath while the `loading project…` live line continues. The header render SHALL be part of the startup budget gate. Content, top to bottom: the crescent wordmark (variant V2, the small crescent lockup, as chosen in the design sign-off) with the version; the model id with its context window; the workspace path with the home directory shortened to `~`; `C# workspace · Roslyn` when a C# workspace is detected; the extension/MCP/skill counts (an `…` placeholder until composition finishes, then exactly one in-place update line — never a redraw of the header block); and a hint line naming `/help`. When composition finds zero of a kind, the counts stay honest (`extensions 0 · MCP 0 · skills 0`).

The header SHALL be capability-aware: the full block only at width ≥ 80 with Unicode support; otherwise a single-line ASCII form (`lunate v0.1.0 (C# · Roslyn) — /help for commands`). `NO_COLOR` SHALL leave the structure unchanged with colors off; a terminal without ANSI support (legacy console, `TERM=dumb`) SHALL get the ASCII form without colors. The header SHALL never appear in print mode, ACP or `--json`. The `startupHeader` setting (default on) and the `LUNATE_STARTUP_HEADER` environment override SHALL suppress it; `/new` SHALL show it again (a session start re-evaluates nothing else).

When no model is configured, or no provider or key resolves, the header SHALL print exactly one actionable fix — no `model` setting and no `LUNATE_MODEL`: `set 'model' in ~/.lunate/settings.json`; no key file: `add the '<provider>' key to ~/.lunate/auth.json`; an environment variable present but not resolving: `set <VARIABLE> to a valid key` — and SHALL print no counts or hint block in that state. The workspace line stays in every state.

#### Scenario: Header prints once on the first frame

- **GIVEN** an interactive session at width ≥ 80 with Unicode and a resolved provider
- **WHEN** the session starts
- **THEN** the header block (wordmark V2, version, model id and context window, `~`-shortened workspace, hint line with `/help`) is printed once into scrollback before composition output, and the `loading project…` line continues

#### Scenario: Counts fill in with a single update line

- **GIVEN** a session whose header printed with the `…` placeholder
- **WHEN** composition completes
- **THEN** exactly one counts line (`extensions N · MCP N · skills N`) is printed below — the header block is not redrawn

#### Scenario: ASCII fallback on narrow or non-Unicode terminals

- **GIVEN** a terminal at width < 80, or without Unicode support
- **WHEN** the session starts
- **THEN** the single-line ASCII form is printed and no block art appears

#### Scenario: First run without a provider

- **GIVEN** no key file and no environment key that resolves
- **WHEN** the session starts
- **THEN** the header names exactly one fix (`add the 'openai' key to ~/.lunate/auth.json`) and prints no counts or hint block

#### Scenario: First run without a model

- **GIVEN** no `model` setting and no `LUNATE_MODEL`
- **WHEN** the session starts
- **THEN** the header names exactly one fix (`set 'model' in ~/.lunate/settings.json`), keeps the workspace line, and prints no counts or hint block

#### Scenario: Machine modes never show it

- **GIVEN** print mode, ACP or `--json`
- **WHEN** a run starts
- **THEN** no welcome header is rendered

#### Scenario: Setting and environment suppress the header

- **GIVEN** `startupHeader` set to `false` (or `LUNATE_STARTUP_HEADER=0`)
- **WHEN** an interactive session starts, and again after `/new`
- **THEN** no header is printed in either case

### Requirement: Thinking display

Reasoning events SHALL render in the live area in the theme's `thinking` color while they stream — prefixed `⠹ thinking… <elapsed>s` and, while the turn is cancellable, the run's token deltas and `esc to interrupt` on the same line — capped at 12 visible lines (older lines scroll away) — and SHALL commit to the scrollback when the reasoning ends: as one collapsed line `✦ thought for <n>s` (whole seconds from the first to the last reasoning event) by default, or as the full wrapped text (indented two spaces) when the `thinkingFull` setting is enabled. Committed thinking SHALL be driven by the event stream alone, so recorded fixtures render identically. Color SHALL never carry meaning alone: the `✦` glyph and the words `thought for` carry it in NO_COLOR and 16-color modes.

#### Scenario: Live then collapsed

- **GIVEN** a run whose model streams reasoning and then text
- **WHEN** the reasoning ends
- **THEN** the live thinking block disappears and a single `✦ thought for <n>s` line commits to the scrollback

#### Scenario: Full text setting

- **GIVEN** the `thinkingFull` setting enabled
- **WHEN** the reasoning ends
- **THEN** the full reasoning text commits wrapped in the thinking color

#### Scenario: Fixtures replay identically

- **GIVEN** a recorded fixture carrying reasoning events
- **WHEN** it is replayed
- **THEN** the thinking live and commit frames are identical to the recording

#### Scenario: The live prefix carries the turn's metrics

- **GIVEN** a running thinking turn with usage events received
- **WHEN** the live prefix renders
- **THEN** it shows the elapsed seconds, the run's `↑<in> ↓<out>` deltas and, while cancellable, `esc to interrupt`, formatted culture-invariantly

### Requirement: Transcript viewer

`Ctrl+O` SHALL open a full-screen transcript viewer over the session's full record — every tool
output, thinking block and diff — rendered in the alternate screen through the `IConsoleIO` seam
and restored on every exit path. The viewer SHALL open at the latest entry and SHALL be navigable
(Up/Down, PageUp/PageDown, Home/End, `g`/`G`); `q`, `Esc` or `Ctrl+O` SHALL close it and return to
the session with the scrollback unchanged. While the viewer is open, input SHALL NOT reach the
model, and the footer's keybinding line SHALL name the binding. The viewer SHALL be lazy: nothing
renders until it is opened.

#### Scenario: Full content on demand

- **GIVEN** a session whose blocks elided output
- **WHEN** `Ctrl+O` opens the viewer
- **THEN** the viewer shows the full output of every block, the thinking blocks and the diffs

#### Scenario: Close restores the screen

- **GIVEN** the viewer open
- **WHEN** `q` or `Esc` closes it
- **THEN** the alternate screen is left, the scrollback is unchanged and the session resumes

#### Scenario: The viewer is modal

- **GIVEN** the viewer open and a turn running
- **WHEN** keys are pressed
- **THEN** they navigate the viewer only and no input reaches the model

#### Scenario: Nothing renders until opened

- **GIVEN** a session that never opens the viewer
- **WHEN** the session runs
- **THEN** no viewer rendering work occurs and the startup budget is unchanged

### Requirement: Input box

The live area SHALL render the input as a box: a full-width rule above the input at every width, a second full-width rule below it at every width, the input's first line carrying the `> ` prompt prefix and continuation lines a two-space indent, and the status footer below the box — below the lower rule when it is drawn, directly below the input when it is not. When the input is empty, the first line SHALL show the placeholder `Ask anything, / for commands` in the theme's `user` colour; the `> ` prompt marker SHALL carry that colour whenever the input renders (plain under `NO_COLOR`), while typed text and continuation lines stay plain. The box SHALL grow with its content to eight lines and scroll beyond that, keeping the cursor's line visible; the rules and placeholder SHALL use ASCII fallbacks (`-`) when Unicode is unavailable, and the lower rule SHALL be drawn at every width so the box always closes — the compact band keeps its other reductions, not this one — while the upper rule, the input and footer stay.

#### Scenario: The box draws rules at 80 or more columns

- **GIVEN** a terminal 120 columns wide
- **WHEN** the input renders
- **THEN** a rule spans the width above and below the input and the footer renders below the lower rule

#### Scenario: No rules below 80 columns

- **GIVEN** a terminal 50 columns wide
- **WHEN** the input renders
- **THEN** the upper rule spans the width, the lower rule is absent, and the `> ` input line and the compact footer render

#### Scenario: The placeholder shows when empty

- **GIVEN** an empty input
- **WHEN** the box renders
- **THEN** the first line shows the prompt marker and `Ask anything, / for commands` in the theme's `user` colour (plain under `NO_COLOR`)

#### Scenario: Growth to eight lines then scroll

- **GIVEN** an input of more than eight wrapped lines
- **WHEN** the box renders
- **THEN** exactly the eight lines around the cursor render and the rest scroll

#### Scenario: ASCII fallback

- **GIVEN** a terminal without Unicode support at 80 or more columns
- **WHEN** the box renders
- **THEN** the rules use `-` runs and no box character leaks

### Requirement: Autocomplete

Typing SHALL open an as-you-type autocomplete list above the input: when the input's first word starts with `/`, the list SHALL show the built-in commands, the `/skill:<name>` entries and the extension commands, each with a short description where one exists; when the cursor sits at the end of an `@` token, the list SHALL show the workspace files matching that token from the file index. The list SHALL be one registry composed from the command table, the skill catalog and the extension manifests — no separate hard-coded lists — filtered fuzzily as a subsequence of the typed query for the fixed registry, while file rows come from the index's case-insensitive completion query. At most eight rows SHALL render, directly above the input; ↑/↓ SHALL move the selection, Tab and Enter SHALL accept it (replacing the token, keeping the `@` for file rows and closing the list), Esc SHALL close the list, and typing SHALL re-filter. Descriptions SHALL be dropped below 60 columns; file rows SHALL render the directory part muted; with no matches the list SHALL stay closed.

#### Scenario: The list opens on slash

- **GIVEN** an empty input
- **WHEN** `/` is typed
- **THEN** the list shows the commands, skills and extension commands with descriptions, capped at eight rows above the input

#### Scenario: Typing filters fuzzily

- **GIVEN** the open list
- **WHEN** `/thk` is typed
- **THEN** the list shows the subsequence matches (for example `/thinking`) and closes when nothing matches

#### Scenario: Arrows and accept

- **GIVEN** the open list with several rows
- **WHEN** ↓ moves to the second row and Tab is pressed
- **THEN** the input becomes that command and the list closes

#### Scenario: Enter accepts instead of submitting

- **GIVEN** the open list
- **WHEN** Enter is pressed
- **THEN** the selected item is accepted and no turn is submitted

#### Scenario: Esc closes the list first

- **GIVEN** the open list
- **WHEN** Esc is pressed
- **THEN** the list closes and the input keeps its text

#### Scenario: File mode lists index matches

- **GIVEN** the input `read @src/co` with the cursor at its end and the index ready
- **WHEN** the list renders
- **THEN** the matching paths appear with the directory part muted and accepting keeps the `@`

#### Scenario: The registry is one source

- **GIVEN** a session with discovered skills and loaded extensions
- **WHEN** the slash list opens
- **THEN** the `/skill:<name>` entries and the extension command names appear alongside the built-ins, each described

#### Scenario: Descriptions drop below 60 columns

- **GIVEN** a terminal 50 columns wide
- **WHEN** the list renders
- **THEN** rows show names only

### Requirement: File index

The workspace file index SHALL be built lazily (never at startup), asynchronously, honouring `.gitignore` files (root and nested; negation, directory-only and anchoring rules) while always skipping `.git`; directory symlinks SHALL not be followed. It SHALL be bounded to 200,000 entries (sorted ordinals; a truncation flag is exposed) and a completion query SHALL be case-insensitive while the index stays sorted ordinally. When the index is not ready yet, the session SHALL show an indexing notice and start the build. On a generated 20,000-file repository the build SHALL stay within 5 seconds and a warm query within 50 milliseconds, proven by a budget check in the gate.

#### Scenario: Ignores are honoured

- **GIVEN** a workspace whose `.gitignore` excludes `bin/` and whose tree contains `bin/app.dll`
- **WHEN** the index is queried for `bin`
- **THEN** no ignored entry is returned and `.git` never appears

#### Scenario: Lazy and bounded

- **GIVEN** a started TUI with no file completion attempted
- **WHEN** the session runs
- **THEN** no index build has started (the startup budget is unaffected); the first `@` completion attempt starts the build and shows the indexing notice, and an over-cap workspace exposes the truncation flag with a deterministic entry set

#### Scenario: The large-repository budget holds

- **GIVEN** the generated 20,000-file budget tree in the gate
- **WHEN** the budget step runs
- **THEN** the build completes within 5 seconds and a warm query within 50 milliseconds

### Requirement: Narrow-terminal breakpoints

The TUI SHALL evaluate three width bands at render time: `≥100` full, `60–99` standard and `<60` compact. In the compact band the tool headers shorten per the tool-block requirement, the footer takes its compact form per the status-footer requirement, the input box keeps its upper and lower rules, and autocomplete descriptions are absent (their ≥60 boundary already covers this). Every rendered line SHALL be no wider than the terminal. A resize SHALL re-render the live area at the new width before any further output, and scrollback committed after the resize SHALL use the new width; already-committed scrollback is immutable and owned by the terminal. When the diff gutter and markdown tables enter the renderers, the compact band SHALL render diffs without the gutter and tables as lists; until then those constructs keep their plain-text forms.

#### Scenario: The full band keeps the full layout

- **GIVEN** a 120-column terminal
- **WHEN** a frame renders
- **THEN** footers carry every segment, tool headers carry their summaries and the box rules are present

#### Scenario: The compact band applies its rules

- **GIVEN** a 40-column terminal
- **WHEN** a frame with a committed tool block and the input renders
- **THEN** the header is short, the footer reads `model · <pct>%`, the upper rule is drawn, the lower rule is absent and every line fits the width

#### Scenario: Nothing renders wider than the terminal

- **GIVEN** any frame at 40×20, 60×30, 80×24 or 120×40
- **WHEN** its lines are measured in cells
- **THEN** no line exceeds the terminal width

#### Scenario: A resize re-renders at the new width

- **GIVEN** a running session at 40 columns
- **WHEN** the terminal resizes to 120 columns
- **THEN** the next frame renders the full-band layout, and resizing back to 60 renders the compact/standard rules for that width

### Requirement: Working indicator

The live area SHALL carry one activity line while work is in flight: a spinner frame, a label, the elapsed seconds and — while the turn is cancellable — the root run's token deltas and `esc to interrupt`. The label SHALL be `thinking…` while reasoning streams, `<tool name> <summary>` while a tool runs, and `working…` otherwise. The narrow band (below 60 columns) SHALL keep the spinner, the label and the elapsed seconds and drop the token deltas and the interrupt hint. The thinking label SHALL carry the thinking colour where the terminal supports colour — the theme value in truecolor and 256-color modes, the 16-color fallback in 16-color mode — and SHALL be colour-neutral under NO_COLOR (the spinner carries the motion).

#### Scenario: Thinking work shows the affordances

- **GIVEN** a running turn whose model streams reasoning
- **WHEN** the activity line renders
- **THEN** it reads `⠹ thinking… <elapsed>s · ↑<in> ↓<out> · esc to interrupt`

#### Scenario: A running tool names itself

- **GIVEN** a running tool call
- **WHEN** the activity line renders
- **THEN** it carries the spinner, the tool name with its argument summary, and the elapsed seconds

#### Scenario: Narrow band keeps the essentials

- **GIVEN** a terminal 40 columns wide with a running tool
- **WHEN** the activity line renders
- **THEN** it fits the width with the spinner, the label and the elapsed seconds, and no token deltas or interrupt hint

#### Scenario: Subagent usage stays out of the deltas

- **GIVEN** a root run with a child (subagent) run
- **WHEN** the child reports its usage
- **THEN** the activity line keeps the root run's deltas while the footer's cumulative total includes the child's tokens

### Requirement: Cache usage in `/context`

The `/context` notice SHALL show the session's accumulated prompt-cache tokens when the provider has reported them: cache reads and cache writes as separate counts, formatted culture-invariantly, in a `cache: read <n>, write <m>` segment. A count the provider never reported, or reported as zero, SHALL be treated as unknown and omitted from the segment; the command SHALL NOT display a zero for an unknown count. The footer line itself SHALL remain unchanged — cache counts belong to the explicit `/context` readout, not to the always-visible status.

#### Scenario: Cache read and write are shown

- **GIVEN** usage events carrying `CachedInputTokenCount = 1200` and `AdditionalCounts["CacheCreationInputTokens"] = 300`
- **WHEN** `/context` runs
- **THEN** the notice contains `cache: read 1.2k, write 300`

#### Scenario: An unknown count is omitted

- **GIVEN** a provider that reports input and output tokens but no cache counts
- **WHEN** `/context` runs
- **THEN** the notice has no cache segment and no invented zeros

#### Scenario: A reported zero is treated as unknown

- **GIVEN** usage events carrying `CachedInputTokenCount = 0` and `AdditionalCounts["CacheCreationInputTokens"] = 0`
- **WHEN** `/context` runs
- **THEN** the notice has no cache segment and no zero counts

#### Scenario: The cache segment is separate from the budget total

- **GIVEN** usage whose `InputTokenCount` already includes the cached tokens
- **WHEN** the session accumulates cache reads and writes
- **THEN** the token total is unchanged and the cache counts are display-only

### Requirement: Cost in the status footer

When every `UsageUpdated` record of the current turn tree (root and subagents) resolves to a catalog model with a known rate for every positive token lane, the footer SHALL show the turn's cost as `$` plus a culture-invariant decimal: four decimals below `$1`, two at or above `$1`, and `<$0.0001` for a positive value that would otherwise round to zero. If any record is unpriced, unknown, or the turn has no usage, the segment SHALL be absent. A new turn SHALL start a fresh cost. The cost SHALL be display-only and SHALL NOT change the token totals, budgets or approvals.

#### Scenario: A fully priced turn shows a cost

- **GIVEN** a turn with input, output and cache usage whose model prices every used lane
- **WHEN** the footer renders
- **THEN** it shows the summed turn cost in dollars

#### Scenario: A subagent's usage is included

- **GIVEN** a turn whose root and child run both report priced usage
- **WHEN** the footer renders
- **THEN** the cost is the sum of both runs' records

#### Scenario: One unpriced record suppresses the turn cost

- **GIVEN** a turn where one routed model has no pricing
- **WHEN** the footer renders
- **THEN** no cost segment appears, even though other records are priced

#### Scenario: Tiny costs never read as zero

- **GIVEN** a priced turn whose cost is positive but below `$0.0001`
- **WHEN** the footer renders
- **THEN** the segment reads `<$0.0001`

### Requirement: Favourites cycling

`Ctrl+P` SHALL switch to the next favourite in settings order and wrap at the end, using the same model-switch path as `/model` (a session model-change entry, a rebuilt harness and an updated footer). When the active model is not in the list, it SHALL switch to the first favourite. An empty list SHALL produce a notice and no switch. The binding SHALL be refused while a turn runs, an approval is open, a picker is open or a secret prompt is open, and SHALL win over an open autocomplete list like `Ctrl+L` and `Ctrl+O`.

#### Scenario: Ctrl+P advances and wraps

- **GIVEN** favourites `[a, b, c]` and active model `a`
- **WHEN** Ctrl+P is pressed three times
- **THEN** the active model moves to `b`, then `c`, then back to `a`, and each switch is recorded in the session

#### Scenario: A non-favourite starts at the first

- **GIVEN** favourites `[a, b]` and active model `z`
- **WHEN** Ctrl+P is pressed
- **THEN** the active model becomes `a`

#### Scenario: An empty list is a notice

- **GIVEN** no favourites
- **WHEN** Ctrl+P is pressed
- **THEN** a notice says to add one with `/favorite` and no run starts

#### Scenario: Ctrl+P waits for idle

- **GIVEN** a running turn
- **WHEN** Ctrl+P is pressed
- **THEN** a notice refuses the switch and the run is untouched

### Requirement: Masked provider login prompt

`/login <provider>` SHALL open a secret prompt in the live area for a known preset: the typed characters SHALL render as a mask, Enter SHALL save through the auth store, and Esc or an empty value SHALL cancel. While the prompt is open, ordinary input SHALL be inert and only prompt/quit keys SHALL act. The secret SHALL NOT appear in the input line's history, the transcript, scrollback, a notice, the session file or a log. A failed save SHALL report the failure without the value. `/logout <provider>` SHALL need no prompt and SHALL NOT make a network call.

#### Scenario: The key is masked and never persisted visibly

- **GIVEN** an idle session
- **WHEN** `/login groq` is submitted, a key is typed and Enter is pressed
- **THEN** the frames show only a mask, the key is stored in auth.json, and the key text appears nowhere in scrollback, history or the session file

#### Scenario: Esc cancels without saving

- **GIVEN** an open secret prompt
- **WHEN** Esc is pressed
- **THEN** the prompt closes, the auth store is unchanged, and a notice says the login was cancelled

#### Scenario: Ordinary input is inert while the prompt is open

- **GIVEN** an open secret prompt
- **WHEN** ordinary text and Enter are pressed
- **THEN** only the secret prompt consumes them and no run starts

#### Scenario: Only presets are accepted

- **GIVEN** an idle session
- **WHEN** `/login not-a-provider` is submitted
- **THEN** a notice lists the supported presets and no prompt opens

### Requirement: Image placeholders in the user block

The committed user block SHALL render the processed text exactly as submitted, including the `[image <width>x<height>]` placeholders produced by image references. The transcript entry SHALL carry the same text, so the scrollback and the `Ctrl+O` viewer never disagree. The block SHALL NOT contain base64 data or a terminal image escape sequence.

#### Scenario: The user block shows the placeholder

- **GIVEN** a submitted message `explain @diagram.png`
- **WHEN** the user block commits
- **THEN** it reads `> explain [image 1024x768]` and contains no image bytes or escape sequence

#### Scenario: The viewer shows the same text

- **GIVEN** the same message in the transcript
- **WHEN** `Ctrl+O` opens the viewer
- **THEN** the user entry reads the same processed text with the placeholder

### Requirement: Notice rendering

A live-area notice SHALL wrap to the terminal width instead of being clipped, so a long message loses no content. Each physical line SHALL be sanitized and SHALL be no wider than the terminal; the whole notice SHALL be capped at four lines and SHALL end its last line with an ellipsis marker when it overflows. The wrapped notice's real line count SHALL feed the live area's budget, so the input box keeps its eight lines and the tail loses the rows instead. A notice that sanitizes to nothing SHALL render as none, and a capped notice SHALL never hide the input.

#### Scenario: A long notice wraps

- **GIVEN** a 40-column terminal
- **WHEN** a notice longer than the width is rendered
- **THEN** it wraps at the width, no word is lost, and every physical line is at most 40 columns

#### Scenario: A pathological notice is capped

- **GIVEN** an 80-column terminal
- **WHEN** a notice longer than four lines is rendered
- **THEN** it occupies four lines, the last ends with the ellipsis marker, and the input box is still fully rendered

### Requirement: Bash mode

At the start of an empty input, `!` SHALL enter bash mode and `!!` SHALL enter it in the silent form; the prompt SHALL render `! ` in the accent colour while the mode is active, and Esc SHALL leave the mode without running, restoring the text with its prefix. Enter SHALL run the remaining line in the session's working directory through the `bash` tool's executor, so the approval policy, shell resolution, environment, timeout, output bounds and process-tree rules are the tool's; the run SHALL commit one tool block whose header carries the exit code and the duration (culture-invariant), and no model request SHALL be made. A `!` that is not the first character of the input SHALL stay literal. A normal `!` submission SHALL add the command and its result to the conversation; a `!!` submission SHALL add neither. Bash lines SHALL be recalled only from bash mode's own history stream, and prompt history SHALL remain unchanged. A `!` submission while a turn is running SHALL be refused with a notice naming the run instead of queueing or steering. Print mode SHALL have no bash mode.

#### Scenario: A command runs and commits a block

- **GIVEN** an idle prompt
- **WHEN** the user types `!echo hi` and presses Enter
- **THEN** the prompt was rendered as `! `, the command runs in the workspace, one tool block commits with `hi` in its body and exit 0 and a duration in its header, and no model request is made

#### Scenario: Approval still decides

- **GIVEN** an approval policy that asks before `Execute` calls
- **WHEN** the user submits `!rm -rf build`
- **THEN** the approval prompt appears before anything runs, and a denial leaves the workspace untouched

#### Scenario: The silent form stays out of the conversation

- **GIVEN** a conversation with messages
- **WHEN** the user submits `!!date`
- **THEN** the block commits to the scrollback and the conversation the next request carries is unchanged

#### Scenario: Escape leaves without running

- **GIVEN** bash mode with `!git status` typed
- **WHEN** Esc is pressed
- **THEN** the mode ends, the text is restored with its prefix, and nothing runs

#### Scenario: A mid-text exclamation stays literal

- **GIVEN** an empty prompt
- **WHEN** the user types `explain the ! operator` and presses Enter
- **THEN** the line is submitted as a prompt and no command runs

#### Scenario: Bash history is separate

- **GIVEN** an earlier `!git status` in this session
- **WHEN** Up is pressed in bash mode, then in prompt mode
- **THEN** bash mode recalls the command and prompt mode recalls only prompts

#### Scenario: A running turn refuses the mode

- **GIVEN** a turn in progress
- **WHEN** the user submits `!ls`
- **THEN** a notice names the running turn, nothing runs, and no command is queued or steered

### Requirement: Follow-up queue

Enter SHALL queue the current input — text and attachments — while a turn runs, clear the line, and show the queue size beside the mode hint; the send key SHALL never interrupt a running turn. Queued messages SHALL run in order, one at a time, through the same submission path as a typed message: when the turn completes, when it fails, and when the user interrupts it with Esc, because the queued messages are what the user wants next. The queue SHALL hold at most five entries and SHALL refuse a sixth with a notice that keeps the existing five. Up with a non-empty queue SHALL take the whole queue back — every entry moved into the editor in order, newline-separated, ahead of any text already typed — and empty the queue; with an empty queue Up navigates history as before. A slash command SHALL be refused by the queue with a notice. The queue SHALL NOT be stored in the session, replayed on resume, or reachable from print mode.

#### Scenario: A queued message runs after the turn

- **GIVEN** a running turn
- **WHEN** the user presses Enter on `run the tests`
- **THEN** the line clears, the indicator shows one queued, and when the turn completes `run the tests` is submitted through the normal path

#### Scenario: Queued messages keep their order and never overlap

- **GIVEN** two queued messages during one turn
- **WHEN** the turn completes
- **THEN** the first runs, and the second starts only after the first completes

#### Scenario: A failed turn keeps the rest of the queue

- **GIVEN** a turn that fails with one message queued
- **WHEN** the failure is reported
- **THEN** the queued message still runs

#### Scenario: An interrupt hands over to the queue

- **GIVEN** a running turn with one message queued
- **WHEN** Esc interrupts the turn
- **THEN** the queued message runs next, in the same order it would have run had the turn completed

#### Scenario: Up takes the queue back

- **GIVEN** two queued messages and `half` typed in the line
- **WHEN** Up is pressed
- **THEN** both messages are in the editor in order, newline-separated, ahead of `half`, and the queue is empty

#### Scenario: Up still recalls history with nothing queued

- **GIVEN** an empty queue and two earlier prompts
- **WHEN** Up is pressed twice
- **THEN** the history entries are recalled in order, not a queue take-back

#### Scenario: A command cannot be queued

- **GIVEN** a running turn and `/quit` in the line
- **WHEN** Enter is pressed
- **THEN** a notice refuses the queue, the line keeps its text, and nothing is queued

#### Scenario: The sixth message is refused

- **GIVEN** five queued messages
- **WHEN** a sixth is sent
- **THEN** a notice names the five-message limit and the first five remain

### Requirement: One key, one meaning

Key events SHALL route to exactly one intent through a single binding catalog: an ordered list of rows carrying a key pattern, an intent, the contexts in which the row applies and a description. Routing SHALL be pure and SHALL consider only the catalog and the active contexts; a key with no applicable row SHALL edit. The prompt SHALL bind: Enter submits, or queues a follow-up when a turn is running; Alt+Enter, Shift+Enter (where the terminal reports it) and Ctrl+J always insert a newline, in every state; Esc interrupts a running turn; Up with a non-empty queue takes the whole queue back into the editor, and otherwise Up/Down navigate history; Ctrl+C interrupts a running turn, clears an idle prompt that holds text, and arms the quit window only when the prompt is empty and idle; Ctrl+D arms the quit window only when the prompt is empty and idle; Ctrl+L forces a full redraw preserving the input and the conversation; Alt+P opens the model picker; Ctrl+R opens history search; Ctrl+G opens the external editor; Ctrl+S stashes or restores the draft; Ctrl+_ undoes the last input edit; Ctrl+O opens the transcript viewer; Tab completes a leading slash command; the line editor binds Ctrl+A and Ctrl+E to line start and end, Ctrl+K and Ctrl+U to kill to end and to start, Ctrl+W to kill the word before the cursor, Alt+D to kill the word after it, Ctrl+Y to yank the last kill, and Alt+B and Alt+F to move by word. Ctrl+P SHALL be unbound. No two rows SHALL overlap within a context, every row SHALL carry a non-empty description, and every binding SHALL have a scripted key test.

#### Scenario: Every binding routes distinctly

- **GIVEN** the catalog's rows
- **WHEN** each row's key is routed in one of its contexts
- **THEN** it produces that row's intent and no other row's, and the catalog has no overlap within a context

#### Scenario: Every row is described

- **GIVEN** the catalog
- **WHEN** it is inspected
- **THEN** every row has a description that names what the key does, and the guide's table carries the same rows

#### Scenario: Ctrl+C interrupts a running turn

- **GIVEN** a running turn
- **WHEN** Ctrl+C is routed
- **THEN** it produces the interrupt intent, the turn is cancelled, and any queued follow-ups remain queued

#### Scenario: Ctrl+C clears or arms when idle

- **GIVEN** an idle prompt with text, then an idle empty prompt
- **WHEN** Ctrl+C is routed in each state
- **THEN** the first clears the input and the second arms the quit window

#### Scenario: Ctrl+L redraws instead of opening a picker

- **GIVEN** a prompt holding text
- **WHEN** Ctrl+L is routed
- **THEN** it produces the redraw intent, the buffer is unchanged, and no picker opens

#### Scenario: Tab completes instead of editing

- **GIVEN** an input that starts with `/`
- **WHEN** Tab is routed
- **THEN** it produces the complete intent, not edit

#### Scenario: Ctrl+P is unbound

- **GIVEN** the prompt is idle
- **WHEN** Ctrl+P is routed
- **THEN** it produces the edit intent and no session action

#### Scenario: The send key sends in both states

- **GIVEN** an idle prompt, then a running turn
- **WHEN** Enter is routed in each state
- **THEN** the idle route submits and the busy route queues the follow-up, and neither produces the edit or interrupt intent

#### Scenario: Every newline key edits in both states

- **GIVEN** an idle prompt, then a running turn
- **WHEN** Alt+Enter, Shift+Enter or Ctrl+J is routed in each state
- **THEN** all six routes insert a newline, and none submits, queues or interrupts
