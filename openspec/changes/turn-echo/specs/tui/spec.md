# tui — turn-echo deltas

## MODIFIED Requirements

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

## ADDED Requirements

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
