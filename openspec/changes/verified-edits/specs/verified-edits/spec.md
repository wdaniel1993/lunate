# Verified edits — the write-path contract

## ADDED Requirements

### Requirement: Staged-content verification

The `write` and `edit` tools SHALL verify staged content before it reaches disk. Verification SHALL
apply only to `.cs` files: the staged text is compiled in memory as a replacement for the file's
text in the workspace's loaded C# solution (the backend's own load, per ADR-0006). The check SHALL
compile the exact text the write would apply — the post-normalization bytes, line endings and
byte-order mark included — and SHALL NOT itself write, restore, or build. A file that is not `.cs`
SHALL be written without a check.

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

A write SHALL be refused only when verification finds compiler errors in the staged text that the
same file's on-disk content does not already have (baseline-compared, same file). The refusal SHALL
be an error result naming the file, the line, the code and the message, each with what to do next.
Pre-existing errors, warnings and analyzer findings SHALL NOT refuse a write; they SHALL be reported
in the result text and the write SHALL proceed. A dependent project's errors SHALL never refuse this
file's write. After two verification refusals of the same file in one run, the next attempt SHALL
proceed unverified with a notice instead of refusing — a refusal is a gate for fixable errors,
never a lock-out.

#### Scenario: An introduced error refuses and teaches

- **GIVEN** staged text that adds a compiler error the file did not have
- **WHEN** the tool runs
- **THEN** the tool returns an error naming the file, the line, the code and the message, with a
  next step, and the file's bytes are untouched

#### Scenario: Pre-existing errors do not refuse

- **GIVEN** staged text with the same compiler errors the on-disk text already has
- **WHEN** the tool runs
- **THEN** the write proceeds and the result reports the existing diagnostics with their positions

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

### Requirement: Budget and loud degradation

Verification SHALL stay within a latency budget calibrated by the ADR-0005 rule (measure, then
ceil); the operative budget is the warm check, not the cold first load. When verification cannot
run (no solution, no SDK, restore required, a partial load without the file's project) or its
ceiling is exceeded, the write SHALL proceed unverified; the tool result SHALL state one line naming
the reason the check was skipped, so a model and a user always learn that a write went out
unverified. A verified result SHALL say that it ran. The degraded path SHALL never be silent, and a
cancelled check SHALL never cancel the write.

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

### Requirement: Where verification lives

Verification SHALL live in the `write`/`edit` tools (`Lunate.Coding`), acquiring the shared
per-workspace C# backend; it SHALL NOT be a `ToolCalling`/`ToolResultReady` hook and SHALL NOT be a
contribution of an extension tool. A hook- or extension-shaped variant SHALL be a separate OpenSpec
change, not a fallback of this one.

#### Scenario: A seconds-long check is not a hook

- **GIVEN** the hook contract's per-handler timeout and slow threshold (default 500 ms)
- **WHEN** the ideas of placing verification into a `ToolCalling` hook are compared with placing it
  in the tool
- **THEN** the tool is chosen: a hook failure policy of block-fail-safe is the opposite of
  unverified-continue, and the check's seconds do not fit the hook's timeout envelope

#### Scenario: The workspace's own backend is reused

- **GIVEN** the per-workspace registry the session's C# tools already use
- **WHEN** a `write`/`edit` call verifies
- **THEN** it acquires one backend per canonical root, and creates no second Roslyn load, no second
  solution and no out-of-process server
