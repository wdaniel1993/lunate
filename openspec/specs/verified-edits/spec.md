# verified-edits Specification

## Purpose
TBD - created by archiving change verified-edits-implementation. Update Purpose after archive.

## Requirements

### Requirement: Verify staged writes through a registered verifier

The host SHALL offer the exact post-normalization staged content to the verifier registered for
the file's manifest-declared pattern, after approval and between read/normalize and apply/write.
The verifier SHALL receive immutable path, baseline-content and staged-content values and SHALL
have no write API. It SHALL return `verified`, `unverified` with a reason, or `refused` with
diagnostics in `StructuredContent`. Only the host SHALL apply the staged content. The first slice
SHALL use the C# verifier registered by `Lunate.Roslyn` for `.cs` files. A file with no matching
verifier SHALL be written without verification.

#### Scenario: The matching verifier runs after approval

- **GIVEN** a `.cs` write with a matching registered verifier and approved write arguments
- **WHEN** the host has read and normalized the content
- **THEN** it calls the verifier before apply/write with the exact staged text and baseline text

#### Scenario: A denied write never calls a verifier

- **GIVEN** a `.cs` write that requires approval and is denied
- **WHEN** the write tool settles
- **THEN** no verifier is called and no file content is written

#### Scenario: A verifier cannot replace staged content

- **GIVEN** a registered verifier returns a result after receiving immutable staged content
- **WHEN** the host handles the result
- **THEN** the verifier has no write API and the host either writes its original staged content or
  refuses it; it never accepts modified content from the verifier

#### Scenario: A non-matching file writes without verification

- **GIVEN** a file with no registered verifier pattern
- **WHEN** `write` or `edit` stages content for it
- **THEN** the file is written as before without invoking a verifier

### Requirement: Compile the edited project and filter to the staged file

The C# verifier SHALL compile the staged text in memory as a replacement in an already-loaded
workspace compilation, with source generators participating. It SHALL NOT initiate a workspace
load. If no `cs_*` call has loaded the workspace, it SHALL return unverified with a
workspace-not-loaded reason. Diagnostics SHALL be filtered to the staged file. In a multi-targeted
project, only the TFM loaded by the workspace SHALL be checked. Dependents' diagnostics SHALL be out
of scope. Verification SHALL NOT build, restore, or write intermediate files.

#### Scenario: A generated member is available to the staged compilation

- **GIVEN** a fixture project with a `[GeneratedRegex]` method
- **WHEN** an edit introduces a call to that generated method
- **THEN** source generators run in the in-memory compilation and the edit is verified, not refused

#### Scenario: A multi-targeted project checks its loaded TFM

- **GIVEN** a fixture project targeting more than one TFM and a workspace loaded for one TFM
- **WHEN** the C# verifier checks staged content
- **THEN** it checks the loaded TFM only and does not check the other target frameworks

#### Scenario: Dependent-project errors do not refuse the staged file

- **GIVEN** the staged file compiles in its project but breaks a dependent project
- **WHEN** the verifier filters diagnostics
- **THEN** the dependent error does not refuse the write

### Requirement: Refuse only an increased multiset of compiler errors

The C# verifier SHALL compare baseline and staged-file compiler errors as multisets keyed by
`(code, message)`, independent of line position. A key refuses only when its staged count is greater
than its baseline count. Warnings and analyzer diagnostics SHALL NOT refuse. In `verify: block`, an
introduced error SHALL produce a refusal whose message lists the diagnostics without advertising
the refusal bypass; the diagnostics SHALL be present as `StructuredContent`.

#### Scenario: A second identical error is introduced

- **GIVEN** baseline content has one error for a `(code, message)` key
- **WHEN** staged content has two errors with that same key
- **THEN** the increased multiset count is introduced and the write is refused

#### Scenario: Existing errors move without increasing their counts

- **GIVEN** baseline and staged content have equal counts for every `(code, message)` key
- **WHEN** diagnostics move to different line positions
- **THEN** the errors remain pre-existing and the write proceeds

#### Scenario: A refusal reports structured diagnostics without the bypass

- **GIVEN** staged content introduces compiler errors in `verify: block`
- **WHEN** the verifier refuses the write
- **THEN** its result has the diagnostics in `StructuredContent` and the refusal message lists the
  errors but does not mention a future bypass

### Requirement: Verification modes and refusal escape

The `verify` setting SHALL accept `block`, `warn` or `off` and default to `block`. `block` SHALL
refuse introduced compiler errors. `warn` SHALL write despite introduced errors and report the
outcome as unverified with the diagnostics. `off` SHALL skip verifier selection and execution. After
two refusals for the same file in one run, a third attempt SHALL write unverified with a notice.

#### Scenario: Warn writes with the diagnostics

- **GIVEN** the verifier finds introduced compiler errors and `verify` is `warn`
- **WHEN** the write is handled
- **THEN** the staged content is written and the result reports unverified with those diagnostics

#### Scenario: Off skips verification

- **GIVEN** a matching verifier and `verify` is `off`
- **WHEN** the write is handled
- **THEN** the verifier is not called and the staged content is written

#### Scenario: The third refusal attempt writes unverified

- **GIVEN** the same file was refused twice during the current run
- **WHEN** a third attempt reaches verification
- **THEN** the host writes it unverified with a notice

### Requirement: Bounded verification, progress and cancellation

The host SHALL bound each verifier call by the calibrated ceiling, initially 10 s and recalibrated
under ADR-0005. A timeout SHALL become `unverified` with a named timeout reason and the staged
content SHALL be written. The interactive live area SHALL show `verifying… <elapsed>` while a
verifier runs. Esc SHALL cancel the run as usual and SHALL NOT write staged content. Verification
SHALL NOT load the C# workspace at startup or from a `.cs` read/edit: ADR-0021 says “Nothing loads
before the first `cs_*` call,” and a `.cs` read/edit is not such a call. Starting that load from a
read/edit is left out; it would need an ADR-0021 amendment, which the maintainer gives. If the
workspace is not already loaded, the verifier SHALL return unverified with a named
workspace-not-loaded reason.

#### Scenario: A timed-out verifier writes unverified

- **GIVEN** a verifier call that exceeds the configured ceiling
- **WHEN** the host cancels the bounded call
- **THEN** the host writes staged content and reports unverified with the elapsed time and timeout
  reason

#### Scenario: Esc cancels without a write

- **GIVEN** a verifier is running for a staged write
- **WHEN** the user presses Esc
- **THEN** the run is cancelled as usual and no staged content is written

#### Scenario: An unloaded workspace degrades without loading

- **GIVEN** no `cs_*` call has loaded the workspace
- **WHEN** a `.cs` file is edited before any `cs_*` call
- **THEN** verification returns unverified because the workspace is not loaded, without starting a
  background or foreground workspace load
