## MODIFIED Requirements

### Requirement: Per-commit verification matrix
CI SHALL build and test the solution on ubuntu, macos and windows runners for every push to `main` and every pull request that changes anything outside the prose paths, running the full verification gate on each OS. A pull request whose changed paths are all prose SHALL instead run the documentation profile on one runner — markdownlint, `openspec validate --all --strict`, the generated-region check and the guard that no build input or test reads the docs tree — because such a change can break neither the build nor a test. The profile SHALL be chosen by a committed script with a self-test, deny-by-default: only `docs/`, `adr/`, `openspec/` and root-level Markdown qualify, and anything else — any change under `.github/workflows/`, either side of a rename, and any path outside those trees — SHALL select the full gate. A profile that cannot be read SHALL select the full gate. Pushes that touch only documentation MAY skip the matrix but SHALL still run the documentation lint.

#### Scenario: Green matrix on a clean push
- **GIVEN** a push that satisfies the gate
- **WHEN** the CI matrix runs
- **THEN** all three runner jobs complete green

#### Scenario: A failing gate turns CI red
- **GIVEN** a push whose tests fail
- **WHEN** the CI matrix runs
- **THEN** the affected runner job fails and the run is red

#### Scenario: Docs-only push still lints
- **GIVEN** a push to `main` that touches only documentation
- **WHEN** the docs-lint workflow runs
- **THEN** the documentation lint completes green, and a lint violation would fail the workflow

#### Scenario: A proposal pull request runs the docs profile
- **GIVEN** a pull request whose changed paths are all under `openspec/changes/<name>/`
- **WHEN** CI runs for the pull request
- **THEN** the docs profile runs `openspec validate --all --strict` and no build, publish or test job runs

#### Scenario: Prose beside code still runs the full matrix
- **GIVEN** a pull request that changes `docs/guide.md` and `src/Lunate.Tui/Interaction/KeyBinding.cs`
- **WHEN** CI decides the profile from the changed paths
- **THEN** the full matrix runs on all three runners

#### Scenario: A workflow change is never prose
- **GIVEN** a pull request that changes `.github/workflows/ci.yml`
- **WHEN** CI decides the profile from the changed paths
- **THEN** the full gate runs, so a change to the gate cannot reduce the gate that checks it

### Requirement: Aggregate merge gate

The CI workflow SHALL expose a single aggregate status check (`ci-ok`) that succeeds only when every verification job the selected profile requires has completed successfully and every job that profile does not select was skipped, so renaming, adding, or removing a matrix leg cannot silently drop required coverage. The repository ruleset SHALL require the aggregate check for `main` — a one-time maintainer configuration outside the workflow's control. The aggregate SHALL report a result on every pull request: the workflow SHALL NOT apply path filters to pull-request events. A required job that is skipped or cancelled SHALL NOT count as success, a job that ran although the profile did not select it SHALL fail the aggregate, and a run whose profile cannot be read SHALL fail rather than pass.

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

#### Scenario: Aggregate green for a prose pull request
- **GIVEN** a pull request whose changed paths are all prose and whose docs leg succeeds
- **WHEN** the aggregate check runs
- **THEN** it completes green while the full matrix legs are skipped

#### Scenario: Aggregate red when the plan is unreadable
- **GIVEN** a run whose plan job produced no usable profile
- **WHEN** the aggregate check runs
- **THEN** it fails instead of passing vacuously
