# ci-pipeline Specification

## Purpose
The continuous integration and release automation contract: every push to main and every pull request is verified on ubuntu, macos and windows runners with the full gate and calibrated budgets, and release tags produce the three single-file binaries (osx-arm64, win-x64, linux-x64) attached to the GitHub release.

## Requirements

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

On `v*` tags, CI SHALL run the full verification gate before publishing and SHALL fail when the tag does not match the version in `Directory.Build.props`; it SHALL publish self-contained single-file binaries for osx-arm64, win-x64 and linux-x64, attach one archive per target to the GitHub release, attach the framework-dependent `dotnet tool` package and the extension package the same tag publishes, and attach `SHA256SUMS` covering every attached asset. A manual dispatch SHALL produce the same artifacts as workflow artifacts without publishing either package.

#### Scenario: Tag produces three artifacts

- **GIVEN** a `v*` tag
- **WHEN** the release workflow runs
- **THEN** the GitHub release carries one archive for each of the three target RIDs

#### Scenario: Dry run without a release

- **GIVEN** a manual workflow dispatch in dry-run mode
- **WHEN** the release workflow runs
- **THEN** the three target artifacts and both packages are produced and uploaded as workflow artifacts

#### Scenario: Tag and version mismatch fails before publishing

- **GIVEN** a tag whose version does not match `<Version>` in `Directory.Build.props`
- **WHEN** the release workflow runs
- **THEN** the gate job fails and nothing is published

#### Scenario: Archives ship with checksums

- **GIVEN** a `v*` tag that passes the gate
- **WHEN** the release completes
- **THEN** `SHA256SUMS` is attached next to the archives, covers every other attached asset, and matches them

#### Scenario: The tool package ships with the release

- **GIVEN** a `v*` tag that passes the gate
- **WHEN** the release completes
- **THEN** the `lunate` `.nupkg` is attached next to the archives

#### Scenario: The extension package ships with the release

- **GIVEN** a `v*` tag that passes the gate
- **WHEN** the release completes
- **THEN** the `Lunate.Extensions.Subagents` `.nupkg` is attached next to the archives and that version was pushed to nuget.org

### Requirement: Code scanning
A CodeQL workflow SHALL analyze the C# code on pushes to `main`, pull requests to `main` and a weekly schedule, uploading results to GitHub code scanning.

#### Scenario: Analysis on a clean change
- **GIVEN** a pull request that satisfies the gate
- **WHEN** the CodeQL workflow runs
- **THEN** the analysis completes green and results are uploaded to code scanning

### Requirement: SDK pinning

The CI, CodeQL and release workflows SHALL install the .NET SDK version pinned in `global.json`. The pin SHALL be one exact version — `rollForward: disable` and `allowPrerelease: false` — so a patch that Microsoft has staged but not published cannot change what the gate installs; the pinned SDK also decides the runtime bundled into the self-contained binary, so the version is a product decision, not only a build input. A newer patch SHALL arrive as a Dependabot pull request against `global.json` (the `dotnet-sdk` ecosystem, weekly), which runs the full gate before merge; when a newer feature band carries fixes that affect the gate (for example test-runner exit behaviour on Windows), the pin SHALL move in its own change with the rationale recorded.

#### Scenario: Workflows resolve the pinned SDK

- **GIVEN** a workflow run on any runner
- **WHEN** the SDK is set up
- **THEN** the installed version is the one pinned in `global.json`

#### Scenario: A staged-but-unpublished patch cannot change the install

- **GIVEN** setup metadata that advertises a patch the download feed does not serve
- **WHEN** a workflow sets up the SDK
- **THEN** the pinned version is installed and the run continues, instead of resolving the phantom patch and failing

#### Scenario: A published patch arrives as a reviewed pull request

- **GIVEN** a newer patch of the pinned feature band published on the feed
- **WHEN** Dependabot's weekly run completes
- **THEN** one pull request bumps `global.json`, and the full gate runs on it before anyone merges

#### Scenario: A gate-relevant band fix moves the pin

- **GIVEN** an SDK feature band newer than the pin carrying a fix that affects gate behaviour
- **WHEN** the pin is bumped
- **THEN** the change records the measured before/after and the workflows install the new version

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
