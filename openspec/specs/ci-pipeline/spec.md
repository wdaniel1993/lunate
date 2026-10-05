# ci-pipeline Specification

## Purpose
The continuous integration and release automation contract: every push to main and every pull request is verified on ubuntu, macos and windows runners with the full gate and calibrated budgets, and release tags produce the three single-file binaries (osx-arm64, win-x64, linux-x64) attached to the GitHub release.

## Requirements

### Requirement: Per-commit verification matrix
CI SHALL build and test the solution on ubuntu, macos and windows runners for every push to `main` and every pull request, running the full verification gate on each OS.

#### Scenario: Green matrix on a clean push
- **GIVEN** a push that satisfies the gate
- **WHEN** the CI matrix runs
- **THEN** all three runner jobs complete green

#### Scenario: A failing gate turns CI red
- **GIVEN** a push whose tests fail
- **WHEN** the CI matrix runs
- **THEN** the affected runner job fails and the run is red

### Requirement: Budget enforcement in CI
CI SHALL fail when a performance budget is exceeded, and SHALL run the gate self-tests so a broken budget check is detected.

#### Scenario: Budget breach turns the gate red in CI
- **GIVEN** the CI matrix job runs `scripts/gate-tests.sh`
- **WHEN** the budget check is broken (a breach is not detected)
- **THEN** the job fails

### Requirement: Windows verification twin
The windows runner SHALL run `scripts/verify.ps1` in addition to the bash gate.

#### Scenario: Windows twin green
- **GIVEN** the windows runner
- **WHEN** `scripts/verify.ps1` runs
- **THEN** it completes with `verify: OK`

### Requirement: Release artifacts
On `v*` tags, CI SHALL publish self-contained single-file binaries for osx-arm64, win-x64 and linux-x64 and attach one archive per target to the GitHub release.

#### Scenario: Tag produces three artifacts
- **GIVEN** a `v*` tag
- **WHEN** the release workflow runs
- **THEN** the GitHub release carries one archive for each of the three target RIDs

#### Scenario: Dry run without a release
- **GIVEN** a manual workflow dispatch in dry-run mode
- **WHEN** the release workflow runs
- **THEN** the three target artifacts are produced and uploaded as workflow artifacts

### Requirement: Code scanning
A CodeQL workflow SHALL analyze the C# code on pushes to `main`, pull requests to `main` and a weekly schedule, uploading results to GitHub code scanning.

#### Scenario: Analysis on a clean change
- **GIVEN** a pull request that satisfies the gate
- **WHEN** the CodeQL workflow runs
- **THEN** the analysis completes green and results are uploaded to code scanning
