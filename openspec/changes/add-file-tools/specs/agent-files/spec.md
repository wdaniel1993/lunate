## ADDED Requirements

### Requirement: Workspace boundary
A `Workspace` SHALL resolve every file-tool path to a canonical absolute path: symlinks resolved for every existing component, the final target deciding whether the path is inside, relative paths combined with the working directory. A path SHALL be accepted only when the canonical target equals an allowed root or sits under it; allowed roots are the working directory plus explicitly configured extra roots. The comparison SHALL be case-insensitive on Windows and macOS and case-sensitive on Linux. Refusals SHALL name the path, the resolved target and the allowed roots.

#### Scenario: A path inside resolves with its display form
- **GIVEN** a workspace rooted at a directory and a file inside it
- **WHEN** a relative or absolute path to that file is resolved
- **THEN** it is accepted, and the display form is relative to the root with forward slashes

#### Scenario: Lexical escape is refused
- **GIVEN** a path that climbs out of the workspace with `..`
- **WHEN** it is resolved
- **THEN** it is refused with an error naming the resolved target and the allowed roots

#### Scenario: A symlink pointing outside is refused
- **GIVEN** a symlink inside the workspace whose target is outside every allowed root
- **WHEN** a path through the symlink is resolved
- **THEN** it is refused

#### Scenario: A symlink pointing inside is allowed
- **GIVEN** a symlink inside the workspace whose target is inside the workspace
- **WHEN** a path through the symlink is resolved
- **THEN** it is accepted, and the canonical target is reported

#### Scenario: Case variants follow the file system
- **GIVEN** a workspace on a case-insensitive file system and a path differing only in case from a real path
- **WHEN** it is resolved
- **THEN** it is accepted; on a case-sensitive file system the same path is refused when it does not exist

#### Scenario: An extra root widens the boundary
- **GIVEN** a workspace with an extra allowed root
- **WHEN** a path under that root is resolved
- **THEN** it is accepted

### Requirement: read tool
`read` SHALL take `path`, `offset` (1-based, default 1) and `limit` (default 2,000, clamped to 2,000), and return the requested window as numbered lines in the exact format `{n,6}|{text}` (number right-aligned to `max(6, digits(total))`, culture-invariant) with the footer `[lines {first}–{last} of {total}, use offset to continue]` when more lines remain. It SHALL refuse: paths outside the workspace; missing files; directories (pointing at `bash ls`); binary files (NUL byte in the first 8,192 bytes, with the size named); `offset < 1`, `limit < 1`, and `offset` past the end (naming the total). Display normalizes line endings (one trailing `\r` stripped), strips a UTF-8 BOM and replaces invalid bytes with U+FFFD; the file itself is never modified. An empty file returns `[empty file]`.

#### Scenario: Numbered lines and the continuation footer
- **GIVEN** a text file with more lines than the limit
- **WHEN** it is read with default parameters
- **THEN** the output contains numbered lines and the footer names the window and total

#### Scenario: Paging with offset and limit
- **GIVEN** a file of many lines
- **WHEN** it is read with an offset and limit
- **THEN** exactly that window is returned and the footer reflects it

#### Scenario: Binary, directory and missing files fail instructively
- **GIVEN** a binary file, a directory and a missing path
- **WHEN** each is read
- **THEN** each returns an error result whose text says what the file is (size for binary) and what to do instead

#### Scenario: Offset past the end is an error
- **GIVEN** a file with fewer lines than the requested offset
- **WHEN** it is read
- **THEN** the error names the offset and the total line count

### Requirement: write tool
`write` SHALL take `path` and `content`, create missing parent directories, write the content exactly as given (UTF-8 without BOM, no newline munging) and return `wrote {n} lines to {relative path} ({created|replaced})` where `n` counts lines (`\n` count plus one when the content does not end with `\n`; 0 for empty content). It SHALL refuse paths outside the workspace and existing directories. `Details` SHALL carry a `WriteDetails` with the relative path, the created/replaced flag, the line count and a unified diff of the change; details are UI-only.

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
