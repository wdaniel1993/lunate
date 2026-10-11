## MODIFIED Requirements

### Requirement: SDK pinning

The CI, CodeQL and release workflows SHALL install the .NET SDK version pinned in `global.json`. The pin SHALL track a supported .NET 10 feature band; when a newer band carries fixes that affect the gate (for example test-runner exit behaviour on Windows), the pin SHALL move in its own change with the rationale recorded.

#### Scenario: Workflows resolve the pinned SDK

- **GIVEN** a workflow that sets up the .NET SDK
- **WHEN** the SDK is set up
- **THEN** the installed version is the one pinned in `global.json`

#### Scenario: A gate-relevant band fix moves the pin

- **GIVEN** an SDK feature band newer than the pin carrying a fix that affects gate behaviour
- **WHEN** the pin is bumped
- **THEN** the change records the measured before/after and the workflows install the new version
