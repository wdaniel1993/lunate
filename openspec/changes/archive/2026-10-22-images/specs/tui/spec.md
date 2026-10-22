## ADDED Requirements

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

## MODIFIED Requirements

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
