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

### Requirement: No build workers outlive a captured gate step

The gate scripts SHALL run with MSBuild node reuse and the MSBuild server disabled (`MSBUILDDISABLENODEREUSE=1`, `DOTNET_CLI_USE_MSBUILD_SERVER=0`) so that build worker processes cannot inherit and hold the stdout pipe of a captured step. A captured test step SHALL end when its test run ends, not when stray workers time out. The settings SHALL be no-ops where node reuse is absent and SHALL NOT change what is built or tested.

#### Scenario: A captured test step ends with its test run

- **GIVEN** the gate runs a captured test step on Windows
- **WHEN** the test run completes
- **THEN** the step ends without waiting for build workers, and the gate's test totals and coverage are unchanged

#### Scenario: The setting is harmless off Windows

- **GIVEN** a gate run on macOS or Linux
- **WHEN** the scripts set the disable variables
- **THEN** the gate behaves exactly as before
