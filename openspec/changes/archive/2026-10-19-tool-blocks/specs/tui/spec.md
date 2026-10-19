## MODIFIED Requirements

### Requirement: Tool block rendering

Finished tool calls SHALL render as scrollback blocks carrying the tool name, an argument summary, a status (running, ok, error) and a bounded excerpt of the output — first and last lines with an elision marker for the middle — in a borderless style. Each header SHALL lead with a status glyph — `✓` ok, `✗` failed (ASCII `+` and `x`) — and SHALL carry a per-tool compact summary with meta: `read` shows its path with the line range and no output, `bash` its command with the exit code and duration and an excerpt of the output's last lines, `edit` and `write` their path with `+N −M` change counts followed by the diff, `cs_*` tools their severity counts with the top items, and extension and MCP tools their name with a short argument summary. Paths in headers and excerpts SHALL be shortened in the middle, never wrapped. The elision marker SHALL carry the hidden-line count and the `ctrl+o` affordance in the warning colour. Every user-derived string SHALL be escaped before markup assembly, and argument summaries SHALL come from a pure, per-tool summarizer with a generic fallback for unknown tools. Child-run (subagent) events SHALL NOT render as scrollback text or blocks of their own; while a child run is live the live area SHALL show a `subagent running` status, the spawning tool's own block carries the child's final result, and child usage still feeds the footer — the child's transcript is inspectable through its child session. The spawning block's carried child result SHALL render as one indented group.

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

## ADDED Requirements

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
