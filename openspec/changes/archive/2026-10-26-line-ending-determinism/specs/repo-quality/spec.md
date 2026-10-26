## ADDED Requirements

### Requirement: Canonical bytes for files Lunate owns

Files Lunate owns or generates — the install record, extension locks, `trust.json`, session files, fixtures, exports and logs — SHALL be written as UTF-8 without a byte-order mark with LF line endings on every platform. Their bytes SHALL NOT depend on the platform or the culture that produced them, and a digest over such a file SHALL be computed over exactly those bytes.

#### Scenario: A record written on one machine verifies on another
- **GIVEN** an install record written on Windows
- **WHEN** its `integrity` digest is recomputed on POSIX
- **THEN** the record verifies

#### Scenario: A lock's bytes carry no carriage return
- **GIVEN** an extension lock serialized with the canonical options
- **WHEN** the bytes are inspected
- **THEN** they contain no `\r`, and writing, reading and re-writing the lock yields identical bytes

#### Scenario: The serializer never follows the platform
- **GIVEN** a machine whose `Environment.NewLine` is CRLF
- **WHEN** any hashed document is serialized
- **THEN** the serializer's `NewLine` is `"\n"` and the digest is unchanged from a POSIX machine

### Requirement: Digests over user-owned content normalize line endings

A digest over content the user owns (single-file extension source, content-hashed skills, the MCP configuration) SHALL be computed over text normalized to LF, so the same content with CRLF and with LF hashes identically. A pure line-ending change SHALL therefore not re-prompt for trust, which the trust documentation states.

#### Scenario: CRLF and LF sources share a digest
- **GIVEN** one extension source with LF line endings and the same source with CRLF
- **WHEN** both are digested
- **THEN** the digests are equal

#### Scenario: A line-ending change does not re-prompt
- **GIVEN** a trusted single-file extension
- **WHEN** only its line endings change
- **THEN** no trust prompt appears and the trust record still matches

### Requirement: Committed fixtures keep their bytes

Committed text that is hashed, compared or generated SHALL be pinned to LF in `.gitattributes`, and test inputs whose line endings are themselves the subject SHALL be marked `-text` so git never converts them. The repository SHALL verify that fixtures outside those byte-exact corpora are LF before pinning them.

#### Scenario: A Windows checkout gets the committed bytes
- **GIVEN** a Windows checkout with `core.autocrlf=true`
- **WHEN** a golden frame is read
- **THEN** it carries LF, exactly as committed

#### Scenario: The edit corpus stays byte-exact
- **GIVEN** the CRLF files under `tests/fixtures/edit-corpus/`
- **WHEN** any platform checks them out
- **THEN** their bytes are unchanged
