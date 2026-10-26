## ADDED Requirements

### Requirement: Writes preserve the user's file

The write and edit tools SHALL preserve the file they change: an existing file keeps its dominant line ending and its byte-order mark, and a file with mixed endings is unified to the dominant one. A new file SHALL follow `.editorconfig` for its directory, then `.gitattributes`, else the platform default. Neither tool SHALL rewrite an untouched part of a file to a different line ending.

#### Scenario: A CRLF file stays CRLF
- **GIVEN** a file whose lines end with CRLF
- **WHEN** the write tool replaces its content
- **THEN** every line still ends with CRLF

#### Scenario: A byte-order mark survives a rewrite
- **GIVEN** a file that begins with a UTF-8 byte-order mark
- **WHEN** the edit tool changes one line
- **THEN** the mark is still there and the file's other bytes are unchanged

#### Scenario: A new file follows the project's convention
- **GIVEN** a directory whose `.editorconfig` sets `end_of_line = lf` on a machine whose default is CRLF
- **WHEN** the write tool creates a file there
- **THEN** the file uses LF

#### Scenario: A mixed file is unified, not multiplied
- **GIVEN** a file with both CRLF and LF lines, mostly CRLF
- **WHEN** the edit tool changes one line
- **THEN** the result uses CRLF throughout and the change is reported as the edit's own
