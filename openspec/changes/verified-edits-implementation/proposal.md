## Why

`write` and `edit` need a verified-edit contract that prevents newly introduced C# compiler errors
from reaching disk without coupling core tools to Roslyn. The maintainer-approved design for #184
settled the behaviour; this change implements it through the public extension surface and records
the work under #336.

## What Changes

- Add a public write-verifier registration API to `Lunate.Extensibility.Abstractions`; extensions
  declare verifier names and file patterns in their manifest.
- Have the host invoke the matched verifier after approval, between read/normalize and apply/write,
  with a 10 s maximum duration and no verifier write access.
- Register the C# verifier from `Lunate.Roslyn`; compile staged text in memory with source
  generators, checking the TFM loaded by the workspace and filtering diagnostics to the edited
  file. Reuse only an already-loaded workspace; otherwise report unverified without initiating a
  workspace load.
- Add `verify: block | warn | off` to user settings, default `block`; compare baseline and staged
  errors as `(code, message)` multisets and allow the third same-file attempt to write unverified
  after two refusals.
- Show `verifying… <elapsed>` in the TUI live area; Esc cancels verification and the run without
  writing. Recalibrate the ceiling by ADR-0005 and pin it in the performance suite.
- Update user-facing documentation only once the corresponding behavior is implemented.

## Capabilities

### New Capabilities

- `verified-edits`: verified, unverified and refused write outcomes; compiler baseline comparison;
  settings, timeout, cancellation and progress behavior.

### Modified Capabilities

- `extensions`: manifest-declared write verifiers and their registration/result contract.
- `agent-config`: persisted `verify` setting and its accepted values/default.
- `tui`: live-area verification progress and normal Esc cancellation behavior.

## Impact

- **Public contract and host:** `src/Lunate.Extensibility.Abstractions`, `src/Lunate.Extensibility`,
  `src/Lunate.Coding`; public API tracking and extension manifest validation.
- **C# verification:** `src/Lunate.Roslyn`, using the existing in-memory workspace and generators.
- **Settings and UI:** `src/Lunate.Agent` settings resolution and `src/Lunate.Tui` live-area rendering.
- **Tests:** extension-contract, coding/write-edit, Roslyn, agent-settings and TUI test projects; use
  repository fixtures, including a generated-regex source and a multi-targeted project.
- **Performance and docs:** performance suite, `docs/guide.md`, `docs/architecture.md`, and the
  extension authoring/manifest reference once the behavior is true. No new NuGet package.
