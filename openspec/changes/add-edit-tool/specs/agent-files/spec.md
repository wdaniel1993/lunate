## ADDED Requirements

### Requirement: edit tool
`edit` SHALL take `path`, `old_text` and `new_text`, and replace `old_text`'s lines with `new_text`'s lines. Matching SHALL run in tiers and stop at the first tier with exactly one match: tier 1 compares file lines to `old_text` lines verbatim; tier 2 compares trimmed forms (trailing whitespace per line ignored, CRLF and LF treated alike). A tier with several matches SHALL be an error listing the start line number of every match and SHALL leave the file unchanged; a tier with none falls through; after tier 2 the tool reports not found. The file SHALL keep its line endings (a CRLF file stays CRLF; `new_text` is re-terminated with the file's endings), its BOM state and its trailing-newline state. The result SHALL read `edited <path> lines <first>–<last> (match: exact|normalized)`; `Details` SHALL carry the line range, the tier and a unified diff. Empty `old_text` and identical `old_text`/`new_text` SHALL be errors, as is a path outside the workspace or a missing file.

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

### Requirement: Edit corpus
The edit tool SHALL be verified by a corpus under `tests/fixtures/edit-corpus/`: one folder per case with `input`, `request.json` (`old_text`, `new_text`) and `expected` or `expected-error.txt`. A runner SHALL apply every case in a temp workspace and assert the resulting file bytes or the exact error output.

#### Scenario: Every corpus case passes
- **GIVEN** the corpus folders
- **WHEN** the runner executes them
- **THEN** every case's file bytes or error output matches its expectation exactly
