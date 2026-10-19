# tui — narrow-terminal deltas

## MODIFIED Requirements

### Requirement: Status footer

The footer SHALL show model, tokens used against the context window with percent, working directory and git branch as one dim line, formatted culture-invariantly. When the thinking level is not `off`, a `think:<level>` segment SHALL appear after the usage segment. When the session's model is a virtual or extension-registered model, the model segment SHALL show the last routed resolution (`auto → deep`), updating as `ModelRouted` events arrive. Extension status widgets, when registered, SHALL append their segments after the core fields in registration order. It SHALL degrade deterministically at narrow widths (branch dropped first, then directory; model and usage survive; widget segments drop before the branch) and omit percent when the window is unknown. Below 60 columns the footer SHALL switch to the compact form `model · <pct>%`, dropping the token counts along with the thinking, directory, branch and widget segments regardless of remaining width. The interactive session SHALL provide a `/context` command showing the context usage plus the resolved subagent limits (concurrency, per-child and per-tree token budgets, the fixed depth) with the current tree usage.

#### Scenario: Narrow width degrades in order

- **GIVEN** a footer too wide for the terminal
- **WHEN** it renders
- **THEN** the branch is dropped first, then the directory, and model and usage remain

#### Scenario: No repository, no branch

- **GIVEN** a working directory outside a git repository
- **WHEN** the footer renders
- **THEN** the branch segment is absent and the rest renders normally

#### Scenario: Widget segments append after the core fields

- **GIVEN** a registered status widget returning a segment
- **WHEN** the footer renders
- **THEN** the segment appears after model, usage, directory and branch, and drops before them at narrow widths

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
- **THEN** it reads `model · <pct>%` with no tokens, window, thinking, directory, branch or widget segments

#### Scenario: An unknown window omits the percent at 40 columns

- **GIVEN** a 40-column terminal whose context window is unknown
- **WHEN** the footer renders
- **THEN** only the model segment appears

### Requirement: Tool block rendering

Finished tool calls SHALL render as scrollback blocks carrying the tool name, an argument summary, a status (running, ok, error) and a bounded excerpt of the output — first and last lines with an elision marker for the middle — in a borderless style. Each header SHALL lead with a status glyph — `✓` ok, `✗` failed (ASCII `+` and `x`) — and SHALL carry a per-tool compact summary with meta: `read` shows its path with the line range and no output, `bash` its command with the exit code and duration and an excerpt of the output's last lines, `edit` and `write` their path with `+N −M` change counts followed by the diff, `cs_*` tools their severity counts with the top items, and extension and MCP tools their name with a short argument summary. Below 60 columns the header SHALL shorten to glyph, name and meta only — `read` carries its line count as the meta, `bash` its exit code and duration, `edit` and `write` their change counts, `cs_*` its severity counts, and the extension/MCP fallback its name alone. Paths in headers and excerpts SHALL be shortened in the middle, never wrapped. The elision marker SHALL carry the hidden-line count and the `ctrl+o` affordance in the warning colour. Every user-derived string SHALL be escaped before markup assembly, and argument summaries SHALL come from a pure, per-tool summarizer with a generic fallback for unknown tools. Child-run (subagent) events SHALL NOT render as scrollback text or blocks of their own; while a child run is live the live area SHALL show a `subagent running` status, the spawning tool's own block carries the child's final result, and child usage still feeds the footer — the child's transcript is inspectable through its child session. The spawning block's carried child result SHALL render as one indented group.

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

## ADDED Requirements

### Requirement: Narrow-terminal breakpoints

The TUI SHALL evaluate three width bands at render time: `≥100` full, `60–99` standard and `<60` compact. In the compact band the tool headers shorten per the tool-block requirement, the footer takes its compact form per the status-footer requirement, the input box rules are absent (their ≥80 boundary already covers this), and autocomplete descriptions are absent (their ≥60 boundary already covers this). Every rendered line SHALL be no wider than the terminal. A resize SHALL re-render the live area at the new width before any further output, and scrollback committed after the resize SHALL use the new width; already-committed scrollback is immutable and owned by the terminal. When the diff gutter and markdown tables enter the renderers, the compact band SHALL render diffs without the gutter and tables as lists; until then those constructs keep their plain-text forms.

#### Scenario: The full band keeps the full layout

- **GIVEN** a 120-column terminal
- **WHEN** a frame renders
- **THEN** footers carry every segment, tool headers carry their summaries and the box rules are present

#### Scenario: The compact band applies its rules

- **GIVEN** a 40-column terminal
- **WHEN** a frame with a committed tool block and the input renders
- **THEN** the header is short, the footer reads `model · <pct>%`, no rules are drawn and every line fits the width

#### Scenario: Nothing renders wider than the terminal

- **GIVEN** any frame at 40×20, 60×30, 80×24 or 120×40
- **WHEN** its lines are measured in cells
- **THEN** no line exceeds the terminal width

#### Scenario: A resize re-renders at the new width

- **GIVEN** a running session at 40 columns
- **WHEN** the terminal resizes to 120 columns
- **THEN** the next frame renders the full-band layout, and resizing back to 60 renders the compact/standard rules for that width
