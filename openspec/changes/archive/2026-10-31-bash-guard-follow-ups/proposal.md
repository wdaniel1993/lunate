## Why

The accepted bash guard closes direct state-root writes, but ADR-0024 explicitly leaves four persistence gaps for follow-up: container mounts, pre-existing hardlinks, an opt-out, and visibility of the active mechanism. This change records those deferred 0.2 requirements against the already-shipped guard so that its protection and status are explicit and testable.

## What Changes

- Classify `bash` commands that mount the configured state root into Docker or Podman as high risk.
- Warn once at startup when a pre-existing hardlink in the state root is also reachable outside it, naming the in-root path.
- Add a setting that disables the guard, with the disabled state visible in the tool result and session header; guarding remains enabled by default.
- Report the resolved mechanism (`sandbox-exec`, `bwrap`, `landlock`, or `none`) and probe success in `/context`, with the same fact in short form in the session header.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `agent-tools`: extend the existing Bash guard and high-risk-call requirements with the four deferred behaviors; this modifies the shipped capability and does not introduce a new one.

## Impact

The behavior is scoped to `ToolRiskResolver`, `Lunate.Coding`'s guard and startup settings, and the TUI `/context` and session-header surfaces. Tests will cover each requirement. ADR-0024 remains the accepted design; this proposal adds no architectural decision, public API, dependency, or session-format change.
