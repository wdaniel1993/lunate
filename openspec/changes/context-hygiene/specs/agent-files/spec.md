# Spec Delta — agent-files

## MODIFIED Requirements

### Requirement: write tool
`write` SHALL take `path` and `content`, create missing parent directories, write the content exactly as given (UTF-8 without BOM, no newline munging) and return `wrote {n} lines to {relative path} ({created|replaced})` where `n` counts lines (`\n` count plus one when the content does not end with `\n`; 0 for empty content). It SHALL refuse paths outside the workspace and existing directories. `Details` SHALL carry a `WriteDetails` with the relative path, the created/replaced flag, the line count and a unified diff of the change; details are UI-only. When the content carries a `[redacted:<name>]` placeholder and the target file currently holds one of that credential name's known values, the write SHALL be refused before the file is touched: writing the placeholder would replace the real value the model was masked from and destroy the credential. The refusal is decided inside the file mutation lock, names the placeholder and the relative path, and says what to do instead (edit around the credential line, or leave the write out). Content whose placeholders name credentials the session does not know, and every write into a file that holds none of the placeholder names' known values, SHALL write normally. When no redactor is bound, the refusal does not exist and the write behaves as before.

#### Scenario: Creating a file creates its parents
- **GIVEN** a path in a directory that does not exist yet
- **WHEN** it is written
- **THEN** the file exists with exactly the given content and the result says `(created)`

#### Scenario: Replacing reports replaced and carries a diff
- **GIVEN** an existing file
- **WHEN** it is written with new content
- **THEN** the result says `(replaced)` and `Details` carries a unified diff between the old and new content

#### Scenario: Content is written exactly as given
- **GIVEN** content with CRLF line endings, no trailing newline and non-ASCII text
- **WHEN** it is written
- **THEN** the file bytes are exactly the UTF-8 encoding of the content, with no BOM and no added or removed newline

#### Scenario: Outside the workspace and directories are refused
- **GIVEN** a path outside the workspace and a path that is a directory
- **WHEN** each is written
- **THEN** each returns an error result and no file is touched

#### Scenario: A placeholder written over the real credential is refused
- **GIVEN** a file whose bytes hold the real value of a credential Lunate knows and content that carries that credential's `[redacted:<name>]` placeholder
- **WHEN** it is written
- **THEN** the result is an error naming the placeholder and the relative path, and the file keeps its exact bytes

#### Scenario: A placeholder into a file without the real value writes normally
- **GIVEN** content carrying a `[redacted:<name>]` placeholder named under a credential Lunate knows and a target file that holds none of that name's values
- **WHEN** it is written
- **THEN** the write applies exactly as before and carries the placeholder

### Requirement: edit tool

`edit` SHALL take `path`, `old_text`, `new_text` and an optional 1-based `start_line`, and replace `old_text`'s lines with `new_text`'s lines. Matching SHALL run in tiers and stop at the first tier with exactly one match: tier 1 compares file lines to `old_text` lines verbatim; tier 2 compares trimmed forms (trailing whitespace per line ignored, CRLF and LF treated alike); tier 3 (`indent`) compares each line with leading and trailing whitespace ignored (a blank `old_text` line maps to a whitespace-only file line, and at least one `old_text` line SHALL be non-blank) and re-indents every non-blank `new_text` line by the leading-whitespace character offset between the first non-blank `old_text` line and its matched file line, positive offsets prepending spaces and negative offsets removing up to that many leading whitespace characters. A tier that finds several matches SHALL, when `start_line` is given, narrow to the matches starting within 3 lines of it; exactly one SHALL apply, anything else SHALL be an error listing the start line number of every match and SHALL leave the file unchanged; a tier with none falls through; after tier 3 the tool reports not found together with the closest region — the window with the most trimmed-equal lines (earliest on ties), omitted when nothing resembles `old_text`. On whitespace-significant files (`.py`, `.yaml`, `.yml`, `.mk` by extension, or a file named `Makefile`, case-insensitively) tier 3 SHALL be refused with an error asking to re-read the file instead. The file SHALL keep its line endings (a CRLF file stays CRLF; `new_text` is re-terminated with the file's endings), its BOM state and its trailing-newline state. The result SHALL read `edited <path> lines <first>–<last> (match: exact|normalized|indent)`; `Details` SHALL carry the line range, the tier and a unified diff. Empty `old_text` and identical `old_text`/`new_text` SHALL be errors, as is a path outside the workspace or a missing file. An edit whose `old_text` or `new_text` carries a `[redacted:<name>]` placeholder SHALL be refused before any matching or mutation, for a name the session knows and for one it does not: a placeholder cannot be mapped back to the value it replaced, so an edit travelling through the marker can never address the text it stands in for. The refusal names the placeholder and says to edit around the credential line instead of through it. An edit carrying no placeholder, and an edit from a tool bound to no redactor, SHALL behave as before.

#### Scenario: A unique exact match applies at tier 1
- **GIVEN** a file and `old_text` matching one block verbatim
- **WHEN** it is edited
- **THEN** the replacement applies, the result names the exact tier and the diff is in `Details`

#### Scenario: A unique normalized match applies at tier 2
- **GIVEN** a file whose matching block differs from `old_text` only in line endings or trailing whitespace
- **WHEN** it is edited
- **THEN** the replacement applies and the result names the normalized tier

#### Scenario: Ambiguity is an error listing every match
- **GIVEN** `old_text` that matches several places
- **WHEN** it is edited
- **THEN** the error lists the start line of every match and the file is unchanged

#### Scenario: The file keeps its endings, BOM and trailing-newline state
- **GIVEN** a CRLF file with a BOM and no trailing newline
- **WHEN** it is edited
- **THEN** the result still uses CRLF, keeps the BOM and adds no trailing newline

#### Scenario: Empty and identical texts are errors
- **GIVEN** an empty `old_text`, or `old_text` equal to `new_text`
- **WHEN** it is edited
- **THEN** each is refused with an error and the file is unchanged

#### Scenario: A missing match is not found
- **GIVEN** `old_text` that matches nowhere
- **WHEN** it is edited
- **THEN** the error reports it was not found and the file is unchanged

#### Scenario: start_line picks among several matches
- **GIVEN** `old_text` matching several places and a `start_line` within 3 lines of exactly one match
- **WHEN** it is edited
- **THEN** that match is replaced and the result names its tier

#### Scenario: start_line that cannot narrow is still an error
- **GIVEN** several matches with none or more than one within 3 lines of `start_line`
- **WHEN** it is edited
- **THEN** the error lists every match's start line and the file is unchanged

#### Scenario: A unique indented match applies at tier 3
- **GIVEN** a block whose indentation differs from `old_text`
- **WHEN** it is edited
- **THEN** the replacement applies with `new_text` re-indented by the offset between the first lines and the result names the indent tier

#### Scenario: Whitespace-significant files refuse tier 3
- **GIVEN** a `.py`, `.yaml`, `.yml` or `Makefile` whose block differs only by indentation
- **WHEN** it is edited
- **THEN** the error says the indent tier is disabled and to re-read the file, and the file is unchanged

#### Scenario: A not-found error shows the closest region
- **GIVEN** `old_text` that is absent while a similar block exists
- **WHEN** it is edited
- **THEN** the error shows the closest region and the file is unchanged

#### Scenario: A placeholder on either side of the edit is refused
- **GIVEN** a redaction placeholder `[redacted:<name>]` in `old_text` or in `new_text`
- **WHEN** it is edited
- **THEN** the result is an error naming the placeholder, the file is unchanged, and the refusal says to edit around the credential line instead of through it

#### Scenario: A placeholder for an unknown name is refused too
- **GIVEN** a `[redacted:<name>]` placeholder whose name is not a credential this session knows
- **WHEN** it is edited
- **THEN** the edit is refused with the same rule and the file is unchanged
