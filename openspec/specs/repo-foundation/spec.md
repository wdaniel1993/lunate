# repo-foundation Specification

## Purpose
TBD - created by archiving change add-repo-skeleton. Update Purpose after archive.

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
