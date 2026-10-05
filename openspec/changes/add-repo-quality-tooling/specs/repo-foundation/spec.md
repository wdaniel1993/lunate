## MODIFIED Requirements

### Requirement: Verification gate
`scripts/verify.sh` SHALL run build, tests, single-file publish, startup budget, formatting (CSharpier), documentation lint and public API checks, and SHALL exit non-zero on any failure.

#### Scenario: Green on the skeleton
- **GIVEN** the skeleton repository
- **WHEN** `scripts/verify.sh` runs
- **THEN** it completes with `verify: OK`

#### Scenario: Budget breach turns the gate red
- **GIVEN** an artificially low startup budget (`BUDGET_MS=1`)
- **WHEN** `scripts/verify.sh` runs
- **THEN** it exits non-zero and reports the startup budget exceeded
