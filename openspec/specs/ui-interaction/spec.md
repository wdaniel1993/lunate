# ui-interaction Specification

## Purpose
User-facing interaction beyond approvals, stated once for every mode: transient notices, the status footer's data, and extension-contributed UI all flow through one seam (`IUserInteraction`) so each mode presents them its own way and machine modes degrade cleanly — stdout stays machine-readable, nothing is silently lost where a human is present, and a broken contribution never breaks a run.

## Requirements

### Requirement: User interaction across modes

Every mode SHALL present ambient user interaction through one seam with per-mode implementations: transient notices (`Notify`), status data (`UpdateStatus`) and extension-contributed blocks (`RenderExtension`). The interactive mode SHALL show notices in the live area, status in the footer and extension blocks as styled output; print mode (text and JSON) SHALL write notices to stderr and treat status and extension blocks as no-ops, keeping stdout pure (the answer, or the event stream); ACP mode SHALL write them to the host log — the protocol has no notice frame. A failing extension contribution SHALL be omitted for that update and reported through the host log, never breaking the run, and SHALL be retried on the next update. Approvals SHALL keep their existing per-mode chain; the seam carries output only.

#### Scenario: A notice reaches each mode the stated way

- **GIVEN** a run that emits a retry, compaction or step-limit notice
- **WHEN** the mode presents it
- **THEN** the interactive mode shows it in the live area, print and JSON modes write a `lunate:` line to stderr, and ACP logs it — stdout stays pure in the machine modes

#### Scenario: Status is a footer in the interactive mode and a no-op elsewhere

- **GIVEN** a status update with model, token usage, directory and branch
- **WHEN** it is presented
- **THEN** the interactive footer renders it culture-invariantly and the machine modes ignore it

#### Scenario: An extension block renders interactively and degrades elsewhere

- **GIVEN** an extension-contributed block for a tool result
- **WHEN** it is presented
- **THEN** the interactive mode renders it with its style mapping and the machine modes skip it (ACP logging it), with no stdout output anywhere

#### Scenario: A failing contribution degrades to omission

- **GIVEN** a status widget or renderer that throws
- **WHEN** it is invoked
- **THEN** the contribution is omitted for that update, the failure is reported through the host log, the run continues, and the next update invokes it again
