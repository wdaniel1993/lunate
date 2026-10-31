# verified-edits Specification

## Purpose
Defines the write-path contract for verifying staged content before writes and edits, including refusal criteria and visible fallback when checks cannot run.

## Requirements

### Requirement: Staged-content verification

The `write` and `edit` tools SHALL offer staged content to a verifier registered through the public
write-verifier extension point for the file patterns declared by that extension's manifest. The
first slice registers a C# verifier for `.cs` files through `Lunate.Roslyn`. The verifier SHALL
compile the exact post-normalization staged text in memory as a replacement in the workspace's
loaded C# project, including source generators. In a multi-targeted project it SHALL check only the
TFM loaded by the workspace; it SHALL NOT check dependents. Verification SHALL NOT write, restore,
build, or modify content. The host calls it after approval between read/normalize and apply/write,
with a bounded duration. A file with no registered verifier match SHALL be written without a check.

#### Scenario: A C# write is compiled before it lands

- **GIVEN** a session in a workspace with a loadable solution
- **WHEN** the `write` tool stages content for a `.cs` file under the workspace root
- **THEN** the staged text is compiled in memory against its project, no workspace file changes and
  no `obj`/`bin` artifact is produced before or instead of the write

#### Scenario: A non-C# file is not verified

- **GIVEN** the same session
- **WHEN** the `write` tool stages content for a `.md` file
- **THEN** no verification occurs and the file lands as before

### Requirement: Only introduced errors refuse

A write SHALL be refused only when the staged file has compiler errors whose `(code, message)`
multiset count exceeds the baseline count for the same file's on-disk content. The refusal SHALL
include the introduced diagnostics as `StructuredContent`, and its human-readable message SHALL
list the errors without advertising the bypass. Pre-existing errors, warnings, analyzer findings,
and dependent-project errors SHALL NOT refuse the write. After two refusals of the same file in one
run, the next attempt SHALL write unverified with a notice. `verify: block | warn | off` SHALL
default to `block`; `warn` SHALL write and report unverified with diagnostics, and `off` SHALL skip
verification.

#### Scenario: An introduced error refuses and teaches

- **GIVEN** staged text that adds a compiler error the file did not have
- **WHEN** the tool runs
- **THEN** the tool returns an error naming the file, the line, the code and the message, with a
  next step, and the file's bytes are untouched

#### Scenario: Pre-existing errors do not refuse

- **GIVEN** staged text with the same compiler errors the on-disk text already has
- **WHEN** the tool runs
- **THEN** the write proceeds and the result reports the existing diagnostics with their positions

#### Scenario: A duplicate introduced error refuses

- **GIVEN** the baseline has one error with key `(code, message)` and the staged file has two
- **WHEN** the verifier compares the diagnostics
- **THEN** the additional instance is treated as introduced and the write is refused

#### Scenario: Warnings never refuse

- **GIVEN** staged text that introduces only warnings (compiler or analyzer) that the file did not
  have
- **WHEN** the tool runs
- **THEN** the write proceeds and the warnings are reported in the result

#### Scenario: Refusal under the mutation queue

- **GIVEN** the `edit` tool resolves, matches and computes its replacement text inside the file
  mutation queue
- **WHEN** verification refuses
- **THEN** no file bytes were written: the checked text and the would-be written text are the same,
  because the queue serializes them into one read–check–write step

#### Scenario: The third attempt escapes repeated refusals

- **GIVEN** the same file has been refused twice in this run
- **WHEN** it is attempted a third time
- **THEN** it is written unverified with a notice and the refusal message itself did not advertise
  this bypass

### Requirement: Budget and loud degradation

Verification SHALL stay within a latency budget recalibrated by the ADR-0005 rule (measure, then
ceil), with a ceiling no greater than 10 s. When verification cannot run or reaches the ceiling, the
write SHALL proceed unverified with a named reason. The TUI live area SHALL show `verifying…
<elapsed>` during verification. Esc during verification SHALL cancel the run as usual and SHALL
NOT write staged content. A verifier timeout SHALL NOT silently degrade or cancel the write.

#### Scenario: A cold backend does not block the turn

- **GIVEN** the session's first edit in a workspace whose C# solution is not yet loaded
- **WHEN** the tool runs the check against the cold backend
- **THEN** the write proceeds unverified with one line naming the reason, and a warm
  `cs_diagnostics` call (or a later verified edit) still surfaces the change's errors afterwards

#### Scenario: A check that overruns its ceiling writes unverified

- **GIVEN** a warm backend whose check runs past the ceiling
- **WHEN** the ceiling elapses and the check is cancelled
- **THEN** the write proceeds unverified with the elapsed time in the result, the write itself is
  never cancelled, and the reason is never silence

#### Scenario: Esc cancels verification without writing

- **GIVEN** verification is running for a staged write
- **WHEN** the user presses Esc
- **THEN** the run is cancelled as usual and the staged content is not written

### Requirement: The public verifier seam

The extension contract SHALL allow registration of verifiers for manifest-declared file patterns.
The host SHALL invoke a matching verifier after approval between read/normalize and apply/write,
with a bounded duration. A verifier SHALL return verified, unverified with a reason, or refused with
diagnostics in `StructuredContent`; it SHALL have no write access and SHALL NOT modify content.
The C# verifier SHALL be registered by `Lunate.Roslyn` through this seam. This is distinct from the
core-internal `BashGuard` precedent.

#### Scenario: Hook and extension tool are not verifier placements

- **GIVEN** the hook contract's per-handler timeout and slow threshold (default 500 ms)
- **WHEN** verification is placed
- **THEN** neither a `ToolCalling` hook nor an extension tool is used; the host calls the public
  verifier seam after approval

#### Scenario: The workspace's own backend is reused

- **GIVEN** the per-workspace registry the session's C# tools already use
- **WHEN** a `write`/`edit` call verifies
- **THEN** `Lunate.Roslyn`'s registered verifier reuses the workspace backend, creates no second
  Roslyn load or solution, and starts no out-of-process server

#### Scenario: Source generators contribute generated members

- **GIVEN** a loaded C# project with a `[GeneratedRegex]` method
- **WHEN** an edit introduces a call to that generated method
- **THEN** source generators run in the in-memory compilation and the edit is verified, not refused

#### Scenario: Only the workspace-loaded TFM is checked

- **GIVEN** a multi-targeted project
- **WHEN** the verifier compiles the staged file
- **THEN** it checks the TFM loaded by the workspace, not the project's other target frameworks

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
