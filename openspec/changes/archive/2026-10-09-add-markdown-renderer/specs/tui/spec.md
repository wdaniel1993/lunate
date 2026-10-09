## ADDED Requirements

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
