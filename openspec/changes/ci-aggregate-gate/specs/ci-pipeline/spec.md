# ci-pipeline — delta for ci-aggregate-gate

## ADDED Requirements

### Requirement: Aggregate merge gate

The CI workflow SHALL expose a single aggregate status check (`ci-ok`) that succeeds only when every verification job has completed successfully, and the repository's required status checks SHALL select that aggregate check, so renaming, adding, or removing a matrix leg cannot silently drop required coverage. The aggregate SHALL report a result on every pull request: the workflow SHALL NOT apply path filters to pull-request events. A needed job that is skipped or cancelled SHALL NOT count as success.

#### Scenario: Aggregate green when every job passes

- **GIVEN** a pull request whose verification jobs all succeed
- **WHEN** the aggregate check runs
- **THEN** it completes green

#### Scenario: Aggregate red when a job fails or is skipped

- **GIVEN** a pull request where any verification job fails or is skipped
- **WHEN** the aggregate check runs
- **THEN** it fails, so the merge stays blocked

#### Scenario: Aggregate reports on every pull request

- **GIVEN** a pull request that touches only documentation paths
- **WHEN** CI runs for the pull request
- **THEN** the aggregate check reports a result (no path filter can leave a required check pending)
