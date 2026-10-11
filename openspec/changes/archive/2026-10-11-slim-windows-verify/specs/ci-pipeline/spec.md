## MODIFIED Requirements

### Requirement: SDK pinning

The CI, CodeQL and release workflows SHALL install the .NET SDK version pinned in `global.json`. The pin SHALL track a supported .NET 10 feature band; when a newer band carries fixes that affect the gate (for example test-runner exit behaviour on Windows), the pin SHALL move in its own change with the rationale recorded.

#### Scenario: Workflows resolve the pinned SDK

- **GIVEN** a workflow run on any runner
- **WHEN** the SDK is set up
- **THEN** the installed version is the one pinned in `global.json`

#### Scenario: A gate-relevant band fix moves the pin

- **GIVEN** an SDK feature band newer than the pin carrying a fix that affects gate behaviour
- **WHEN** the pin is bumped
- **THEN** the change records the measured before/after and the workflows install the new version

## ADDED Requirements

### Requirement: Captured gate steps end with their commands

The gate scripts SHALL capture step output without depending on pipe EOF — writing to a file and printing it when the command ends — so a captured step ends when its command ends, even when build workers that inherited its output outlive it (MSBuild node-reuse workers and the compiler server do, by design). The zero-test guard SHALL parse the same captured output.

#### Scenario: A captured test step ends with its test run

- **GIVEN** the gate runs a captured test step on Windows
- **WHEN** the test run completes
- **THEN** the step ends without waiting for surviving build workers, and the gate's test totals and coverage are unchanged

#### Scenario: The capture still guards discovery

- **GIVEN** a test run whose output lacks a non-zero MTP summary
- **WHEN** the captured step completes
- **THEN** the gate fails with the discovery-regression error
