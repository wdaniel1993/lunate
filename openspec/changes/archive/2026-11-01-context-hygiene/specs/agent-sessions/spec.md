# Session deltas — context hygiene

## ADDED Requirements

### Requirement: Tool-output spill files

A session SHALL own the directory `<sessions-dir>/<session-id>/tool-output/`, beside its image blobs, holding the full text of tool results the loop admitted as a pointer. Files SHALL be named `<run>-<seq>.txt` from the run id and the call's deterministic sequence, SHALL be LF and UTF-8 without BOM on every platform, SHALL be owner-only (`0600`, directory `0700`) where the platform has file modes, and SHALL NOT exceed 64 MB. A write SHALL go to a temporary file in the same directory and move into place atomically; a failed write SHALL leave no partial file and SHALL NOT fail the tool. The session file SHALL store only the admitted text the model saw — the pointer, not the full transcript — and SHALL remain schema 3; the spill directory is auxiliary, never transcript state. A run without a session SHALL write no spill file. Loading a session SHALL leave its spill files in place, so a resumed run can read the path a marker names; the directory is not swept like the image store.

#### Scenario: A spilled result is session-owned

- **GIVEN** a session run whose tool result is over the spill threshold
- **WHEN** the files are inspected
- **THEN** the full text is under `<sessions-dir>/<session-id>/tool-output/<run>-<seq>.txt`, the directory is owner-only, and the session file stores only the pointer the model saw

#### Scenario: The bytes do not depend on the platform

- **GIVEN** a spill file written on Windows and the same result written on Linux
- **WHEN** the bytes are compared
- **THEN** both are LF and UTF-8 without BOM and are identical for identical text

#### Scenario: A resumed run can still read the file

- **GIVEN** a session with a spilled result
- **WHEN** the session is loaded again and the marker's path is read
- **THEN** the file is still there and its content is the full masked text

#### Scenario: No session writes nothing

- **GIVEN** a `--no-session` run whose result is over the spill threshold
- **WHEN** the run completes
- **THEN** the sessions directory contains no `tool-output` directory for it

#### Scenario: A failed write leaves no partial file

- **GIVEN** a spill write that fails
- **WHEN** the directory is inspected
- **THEN** no temporary or partial file remains and the tool result was still delivered

### Requirement: Context accounting

The session SHALL record the provider's reported usage for each assistant message against the request as it was actually sent — after masking, `FullOutput` projection, spill pointers and stale-read notices. It SHALL NOT record an estimate of its own, and `/session` and the footer totals SHALL keep their current shape: the recorded input, output, cache-read and cache-write lanes. A tool result that admitted a pointer SHALL therefore show a smaller input count than the same result admitted in full, and the difference SHALL be attributable without hand-written character counts.

#### Scenario: A pointer costs fewer recorded input tokens

- **GIVEN** the same tool result admitted once in full and once as a pointer
- **WHEN** the two assistant entries are compared
- **THEN** the pointer entry records fewer input tokens, and the difference equals the provider's count for each request as sent

#### Scenario: The session never stores an estimate

- **GIVEN** a run whose request is estimated at 12,000 tokens
- **WHEN** the provider reports 11,240 input tokens
- **THEN** the session entry records 11,240, not 12,000

#### Scenario: A stale notice reduces the next request

- **GIVEN** a read result that a later mutation superseded
- **WHEN** the next assistant entry is recorded
- **THEN** its input count is smaller than the request that carried the full read, and the session still holds the original read entry
