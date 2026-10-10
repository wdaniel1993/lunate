# agent-files Specification

## Purpose
The workspace boundary for the file tools — canonical path resolution (symlinks included), the allowed-roots check with its per-platform case rules — and the `read`, `write` and `edit` tools' parameters, output shapes and instructing errors.

## Requirements

### Requirement: Workspace boundary
A `Workspace` SHALL resolve every file-tool path to a canonical absolute path: symlinks resolved for every existing component, the final target deciding whether the path is inside, relative paths combined with the working directory. A path SHALL be accepted only when the canonical target equals an allowed root or sits under it; allowed roots are the working directory plus explicitly configured extra roots. The comparison SHALL be case-insensitive on Windows and macOS and case-sensitive on Linux. Refusals SHALL name the path, the resolved target and the allowed roots. The workspace SHALL expose its identity: `WorktreeRoot` (the canonical run checkout), and — detected from the file system without running git — `RepoRoot` (the main worktree's root) and `GitCommonDir` (the shared git directory), both null outside a repository. Boundary checks SHALL use `WorktreeRoot`; worktrees of one repository SHALL be separate boundaries unless explicitly granted as extra roots.

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

#### Scenario: A dangling symlink pointing outside is refused
- **GIVEN** a symlink whose target does not exist and lies outside every allowed root
- **WHEN** a path through the symlink is resolved
- **THEN** it is refused — a write through it must not create the target outside the boundary

#### Scenario: A symlink cycle is a resolution error
- **GIVEN** symlinks forming a cycle
- **WHEN** a path through them is resolved
- **THEN** the resolution fails with an error instead of hanging or overflowing

#### Scenario: Case variants follow the file system
- **GIVEN** a workspace on a case-insensitive file system and a path differing only in case from a real path
- **WHEN** it is resolved
- **THEN** it is accepted; on a case-sensitive file system the same path is refused when it does not exist

#### Scenario: An extra root widens the boundary
- **GIVEN** a workspace with an extra allowed root
- **WHEN** a path under that root is resolved
- **THEN** it is accepted

#### Scenario: Repository identity is detected from the file system
- **GIVEN** a linked worktree (a `.git` file with `gitdir:` and a `commondir` file)
- **WHEN** a workspace is created for it
- **THEN** `WorktreeRoot`, `RepoRoot` and `GitCommonDir` are reported — without running git

#### Scenario: A non-repository workspace has no identity
- **GIVEN** a directory without `.git`
- **WHEN** a workspace is created for it
- **THEN** the identity members are null and the boundary still works

#### Scenario: Worktrees of one repository are separate boundaries
- **GIVEN** two worktrees of one repository
- **WHEN** a path in the other worktree is resolved from one of them
- **THEN** it is refused unless the other worktree is explicitly granted as an extra root

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

### Requirement: edit tool

`edit` SHALL take `path`, `old_text`, `new_text` and an optional 1-based `start_line`, and replace `old_text`'s lines with `new_text`'s lines. Matching SHALL run in tiers and stop at the first tier with exactly one match: tier 1 compares file lines to `old_text` lines verbatim; tier 2 compares trimmed forms (trailing whitespace per line ignored, CRLF and LF treated alike); tier 3 (`indent`) compares each line with leading and trailing whitespace ignored (a blank `old_text` line maps to a whitespace-only file line, and at least one `old_text` line SHALL be non-blank) and re-indents every non-blank `new_text` line by the leading-whitespace character offset between the first non-blank `old_text` line and its matched file line, positive offsets prepending spaces and negative offsets removing up to that many leading whitespace characters. A tier that finds several matches SHALL, when `start_line` is given, narrow to the matches starting within 3 lines of it; exactly one SHALL apply, anything else SHALL be an error listing the start line number of every match and SHALL leave the file unchanged; a tier with none falls through; after tier 3 the tool reports not found together with the closest region — the window with the most trimmed-equal lines (earliest on ties), omitted when nothing resembles `old_text`. On whitespace-significant files (`.py`, `.yaml`, `.yml`, `.mk` by extension, or a file named `Makefile`, case-insensitively) tier 3 SHALL be refused with an error asking to re-read the file instead. The file SHALL keep its line endings (a CRLF file stays CRLF; `new_text` is re-terminated with the file's endings), its BOM state and its trailing-newline state. The result SHALL read `edited <path> lines <first>–<last> (match: exact|normalized|indent)`; `Details` SHALL carry the line range, the tier and a unified diff. Empty `old_text` and identical `old_text`/`new_text` SHALL be errors, as is a path outside the workspace or a missing file.

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

### Requirement: Edit corpus

The edit tool SHALL be verified by a corpus under `tests/fixtures/edit-corpus/`: one folder per case with `input`, `request.json` (`old_text`, `new_text`, optional `start_line` and `file_name`) and `expected` or `expected-error.txt`. A runner SHALL apply every case in a temp workspace and assert the resulting file bytes or the exact error output.

#### Scenario: Every corpus case passes
- **GIVEN** the corpus folders
- **WHEN** the runner executes them
- **THEN** every case's file bytes or error output matches its expectation exactly
