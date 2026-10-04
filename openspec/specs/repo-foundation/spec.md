# repo-foundation Specification

## Purpose
The repository's build-and-verification contract: every Lunate project builds on .NET 10 with warnings as errors, the project graph stays downward-only, the core libraries' public surface is tracked, and one command (`scripts/verify.sh`) decides whether the repository is done. Later capabilities extend this contract rather than rebuilding it.

## Requirements

### Requirement: Clean build contract
The repository SHALL build all projects on .NET 10 with compiler warnings treated as errors.

#### Scenario: Clean checkout builds
- **GIVEN** a clean checkout with the pinned .NET SDK installed
- **WHEN** `dotnet build` runs
- **THEN** every project compiles with zero warnings and the command exits zero

#### Scenario: A warning fails the build
- **GIVEN** a source change that introduces a compiler warning
- **WHEN** `dotnet build` runs
- **THEN** the build fails and reports the warning as an error

### Requirement: Enforced layering
Project references SHALL point only downward in the architecture: `Lunate.Ai` ← `Lunate.Agent` ← `Lunate.Protocols` / `Lunate.Coding`, and `Lunate.Tui` SHALL reference no other Lunate project.

#### Scenario: Upward reference is rejected
- **GIVEN** a project graph containing an upward reference, e.g. `Lunate.Ai` → `Lunate.Agent`
- **WHEN** the architecture test runs
- **THEN** it fails and names the offending reference

#### Scenario: The real graph passes
- **GIVEN** the repository's actual project graph
- **WHEN** the architecture test runs
- **THEN** it passes

### Requirement: Version entry point
`Lunate.Coding` SHALL provide `lunate --version`, printing the product version and exiting zero within the startup budget.

#### Scenario: Version flag on the published binary
- **GIVEN** the published single-file `lunate` binary
- **WHEN** it is invoked with `--version`
- **THEN** it prints the version, exits 0, and startup stays within the configured budget

### Requirement: Verification gate
`scripts/verify.sh` SHALL run build, tests, single-file publish, startup budget, format check and public API check, and SHALL exit non-zero on any failure.

#### Scenario: Green on the skeleton
- **GIVEN** the skeleton repository
- **WHEN** `scripts/verify.sh` runs
- **THEN** it completes with `verify: OK`

#### Scenario: Budget breach turns the gate red
- **GIVEN** an artificially low startup budget (`BUDGET_MS=1`)
- **WHEN** `scripts/verify.sh` runs
- **THEN** it exits non-zero and reports the startup budget exceeded

### Requirement: Public API visibility
Library projects SHALL track their public surface in `PublicAPI.Shipped.txt` / `PublicAPI.Unshipped.txt`, and verification SHALL fail when a `Shipped` file changes.

#### Scenario: Shipped API change is caught
- **GIVEN** a modification to any `PublicAPI.Shipped.txt`
- **WHEN** `scripts/verify.sh` runs
- **THEN** it exits non-zero and shows the diff

### Requirement: Calibrated performance budgets
The startup budget SHALL be calibrated per environment from measured baselines (dev machines and each CI runner) and re-calibrated when a baseline shifts; budgets SHALL keep documented headroom above the measured median so they catch regressions rather than machine noise.

#### Scenario: Budgets reflect measured baselines
- **GIVEN** measured startup medians for the dev machine and each CI runner (spike S-3)
- **WHEN** the budgets are set in `scripts/perf.sh` and the CI matrix
- **THEN** each budget sits above its measured median with documented headroom

#### Scenario: A regression trips the budget
- **GIVEN** a change that materially regresses startup (for example, doubling the median)
- **WHEN** the verification gate runs
- **THEN** the budget is exceeded and the gate fails

#### Scenario: Re-calibration is documented
- **GIVEN** a baseline shift (new hardware or a runner image change)
- **WHEN** the budgets are updated
- **THEN** the new measurements and the reason for the change are recorded in the spike report
