# Verified edits — the write-path contract

## ADDED Requirements

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
