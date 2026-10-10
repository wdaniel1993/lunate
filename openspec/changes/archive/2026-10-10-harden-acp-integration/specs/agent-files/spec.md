## MODIFIED Requirements

### Requirement: read tool

`read` SHALL take `path`, `offset` (1-based, default 1) and `limit` (default 2,000, clamped to 2,000), and return the requested window as numbered lines in the exact format `{n,6}|{text}` (number right-aligned to `max(6, digits(total))`, culture-invariant) with the footer `[lines {first}–{last} of {total}, use offset to continue]` when more lines remain. It SHALL refuse: paths outside the workspace; missing files; directories (pointing at `bash ls`); binary files (a NUL byte — the first 8,192 bytes of a local read, or anywhere in the text a client-backed read received — with the size named); `offset < 1`, `limit < 1`, and `offset` past the end (naming the total). Display normalizes line endings (one trailing `\r` stripped), strips a UTF-8 BOM and replaces invalid bytes with U+FFFD; the file itself is never modified. An empty file returns `[empty file]`.

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
