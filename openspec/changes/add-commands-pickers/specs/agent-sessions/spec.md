## ADDED Requirements

### Requirement: Session listing

The store SHALL list a session directory for the picker: one entry per `.jsonl` file — session id, created (from the header) and last modified — ordered by last-modified newest first with the id as tie-break, bounded to the most recent 20, skipping files whose header cannot be read. Listing SHALL NOT modify or rewrite any file.

#### Scenario: Newest first with a bound

- **GIVEN** more session files than the cap in a directory
- **WHEN** the directory is listed
- **THEN** at most 20 entries return, newest first, with unique ids

#### Scenario: A corrupt file is skipped

- **GIVEN** a `.jsonl` whose header is unreadable
- **WHEN** the directory is listed
- **THEN** the file is skipped and the rest still list
