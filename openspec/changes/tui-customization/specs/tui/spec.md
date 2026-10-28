## MODIFIED Requirements

### Requirement: Markdown subset rendering

Assistant text SHALL be parsed with a CommonMark-compliant parser (Markdig) and rendered by the product's own renderer into Spectre renderables. The subset covers ATX headings 1-6, paragraphs, bullet and ordered lists (nested), blockquotes, fenced code with a language label, `mermaid` fences routed to the diagram requirement below, and inline bold, italic and code. Every user string SHALL be escaped before markup assembly — bracket text renders literally, never as Spectre markup. Unsupported constructs — tables, links, images, HTML, task lists — SHALL render as readable plain text, never as markup and never throwing. Keyword highlighting covers C#, JSON and shell only, implemented in-product without additional packages.

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

#### Scenario: A mermaid fence reaches the diagram renderer

- **GIVEN** a fenced code block whose language label is `mermaid`
- **WHEN** the document renders
- **THEN** the block is rendered by the diagram renderer, never as source code and never as a markup error

## ADDED Requirements

### Requirement: User keymap

The TUI SHALL accept a user keymap file (`~/.lunate/keymap.json`, `$LUNATE_KEYMAP`, or `--keymap <path>`, in that order of precedence behind the flag) that overlays the shipped `keys.json` catalogue by binding id. A user binding with a catalogue id SHALL replace that binding's keys only, keeping its intent, context and description. A binding id absent from the catalogue SHALL fail the load with the id and up to three nearest catalogue ids named, and a user binding that invents an id SHALL be refused. An empty key list SHALL disable that binding. When the user file and the catalogue bind the same key in the same context to different intents the user file SHALL win and startup SHALL print one line naming both ids; two user bindings in conflict SHALL fail the load.

#### Scenario: A rebound key reaches its intent

- **GIVEN** a user keymap that moves `enter.submit.idle` from Enter to `Ctrl+J`
- **WHEN** the session starts and the user presses `Ctrl+J`
- **THEN** the message is sent, and Enter no longer submits in the idle context

#### Scenario: An unknown id names the near misses

- **GIVEN** a keymap that names `enter.submit.iddle`
- **WHEN** the file is loaded
- **THEN** the load fails, the message names `enter.submit.iddle` and the catalogue ids nearest to it

#### Scenario: A user conflict with the catalogue is reported, not hidden

- **GIVEN** a user keymap that binds Enter in the idle context to `Abort`
- **WHEN** the session starts
- **THEN** the user binding wins and one line names both the user binding and `enter.submit.idle`

#### Scenario: An empty key list disables a binding

- **GIVEN** a user keymap whose `ctrl-d.quit` carries an empty key list
- **WHEN** the user presses `Ctrl+D` twice
- **THEN** the session does not quit

### Requirement: Theme tokens

Colour use SHALL be routed through a semantic token set — `accent`, `muted`, `error`, `success`, `warning`, `code`, `diff.add`, `diff.remove`, `prompt`, `notice` — with three built-in themes (`default`, `ansi16`, `mono`) and a user file (`~/.lunate/theme.json`) that maps those tokens to an ANSI colour name, a 256-colour index or a hex value. The renderer SHALL downgrade by the detected colour capability: hex to the nearest 256-colour entry, then to the nearest of the sixteen, then to no colour. An unknown token SHALL fail the load with the valid tokens listed. The existing `NO_COLOR` and capability rules SHALL take precedence over any theme, so a theme can never re-enable colour in a colourless session. Selection order SHALL be `--theme`, then the `tui.theme` setting, then `default`.

#### Scenario: A hex colour is downgraded to the terminal

- **GIVEN** a theme mapping `accent` to `#ff8800` and a 256-colour terminal
- **WHEN** an accented element renders
- **THEN** it renders in the nearest 256-colour entry, not in raw hex escape codes

#### Scenario: NO_COLOR beats the theme file

- **GIVEN** `NO_COLOR` set and a theme that colours every token
- **WHEN** the session renders
- **THEN** no colour escape is emitted anywhere

#### Scenario: An unknown token lists the valid ones

- **GIVEN** a theme file containing `"highlight": "red"`
- **WHEN** it is loaded
- **THEN** the load fails and the message lists the ten valid tokens

### Requirement: Full-screen session mode

The session SHALL offer an alternate-screen mode, enabled by `--fullscreen` or the `tui.fullscreen` setting, with inline rendering remaining the default. The alternate screen SHALL be entered only after the first frame has been painted and SHALL be left on every exit path — a normal quit, `Ctrl+C`, and an unhandled exception — so the terminal is never left in the alternate buffer.

#### Scenario: Inline stays the default

- **GIVEN** no flag and no setting
- **WHEN** the session starts
- **THEN** the alternate screen is never entered

#### Scenario: The alternate screen is left on an exception

- **GIVEN** full-screen mode enabled and a turn that throws
- **WHEN** the session ends
- **THEN** the leave sequence has been written and the terminal is back on its normal buffer

### Requirement: Mermaid diagram rendering

A fenced code block labelled `mermaid` SHALL be rendered as a diagram for two diagram types — `flowchart`/`graph` with `TD`, `TB` or `LR` direction, labelled nodes and edges with and without labels, and `sequenceDiagram` with participants, `->`, `-->`, `->>` messages and `Note`. Rendering SHALL use Unicode box-drawing characters, or their ASCII stand-ins when the capability system reports no Unicode, with identical geometry. A diagram SHALL render only when its widest line fits the terminal width and the terminal is at least 40 columns wide. An unsupported diagram type, a diagram that does not fit, and a parse failure SHALL each render the diagram source in a fenced code block followed by exactly one line beginning `mermaid: ` that states the reason. Rendering SHALL never throw and SHALL never produce a partial diagram.

#### Scenario: A flowchart renders as a drawing

- **GIVEN** a `flowchart TD` with three labelled nodes and two edges
- **WHEN** the document renders at 100 columns
- **THEN** the output shows three boxes joined by arrows and the source is not shown

#### Scenario: An unsupported type falls back to the source

- **GIVEN** a `stateDiagram-v2` fence
- **WHEN** the document renders
- **THEN** the source renders in a code block followed by one `mermaid: ` line naming the unsupported type

#### Scenario: A diagram wider than the terminal falls back

- **GIVEN** a flowchart 74 columns wide and a terminal 58 columns wide
- **WHEN** the document renders
- **THEN** the source renders with one line stating that the diagram does not fit

#### Scenario: A parse failure names the reason and the line

- **GIVEN** a `sequenceDiagram` with an unterminated participant statement
- **WHEN** the document renders
- **THEN** the source renders with one `mermaid: ` line carrying the parser's message and line number

#### Scenario: ASCII mode keeps the layout

- **GIVEN** Unicode support off and a supported flowchart
- **WHEN** it renders
- **THEN** the boxes and arrows use `+`, `-`, `|`, `<`, `>` and the diagram's geometry is unchanged
