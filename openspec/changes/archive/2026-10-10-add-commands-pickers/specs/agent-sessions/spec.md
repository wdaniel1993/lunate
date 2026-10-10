## ADDED Requirements

### Requirement: Session listing

The store SHALL list a session directory for the picker: one entry per `.jsonl` file — session id, created (from the header) and last modified — ordered newest first by last-modified, then by creation time, then by session id, the creation time and id tie-breaks both descending, bounded to the most recent 20, skipping files whose header cannot be read. Listing SHALL NOT modify or rewrite any file.

#### Scenario: Newest first with a bound

- **GIVEN** more session files than the cap in a directory
- **WHEN** the directory is listed
- **THEN** at most 20 entries return, newest first, with unique ids

#### Scenario: Equal modification times order deterministically

- **GIVEN** session files sharing a modification time (for example two sessions created in the same second)
- **WHEN** the directory is listed
- **THEN** the order is deterministic: newest creation time first, then the id descending

#### Scenario: A corrupt file is skipped

- **GIVEN** a `.jsonl` whose header is unreadable
- **WHEN** the directory is listed
- **THEN** the file is skipped and the rest still list
