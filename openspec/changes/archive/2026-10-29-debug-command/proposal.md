## Why

`/debug` merged in #275 with no requirement in `openspec/specs/tui/spec.md`: the interactive command
surface lists every other session command, but not this one. The behaviour exists and is tested; the
spec — the source of truth for behaviour — does not describe it, so a future change could alter the
destination, the sections or the credential rule without noticing that it was ever promised.

## What Changes

- Add one requirement to the `tui` capability for `/debug [path]`: one text file, by default under
  the state root and outside the workspace, whose absolute destination is named in the output.
- State the sections the bundle carries: version and runtime, the resolved model and provider, the
  state root's path layout (paths and existence, never contents), the extension graph with its
  trust, source and lock digests, the session facts, and the last N events with their timings.
- Make the safety rule explicit and testable: every rendered byte passes `CredentialRedactor`
  before the file is opened, and endpoints print without userinfo or query.
- Name what the bundle never carries: conversation text, file contents, headers and any credential
  the redactor does not know.
- Pin the safety rule with a scenario that plants a known credential and asserts both its absence
  and the presence of `[redacted:<name>]`, so a future `/debug` change cannot drop the redaction
  silently.

No behaviour changes: the requirement describes what #275 shipped. This change is documentation
only — the archive of the delta into the main spec is its only implementation step.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `tui`: adds the `/debug` diagnostic-bundle requirement with its destination, sections, redaction
  rule, scenarios and exclusions.

## Impact

- `openspec/specs/tui/spec.md` gains one requirement when this change is archived; no source, test
  or documentation changes.
- No public API, no dependency and no session-format change.
- The delta matches the merged implementation in `src/Lunate.Coding/DiagnosticBundle*.cs`,
  `src/Lunate.Coding/InteractiveSession.Diagnostics.cs` and the behaviour described in
  `docs/spec/diagnostics.md`, not the original PR description.
