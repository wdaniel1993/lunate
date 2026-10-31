## MODIFIED Requirements

### Requirement: Release smoke

The repository SHALL provide a `smoke.yml` workflow, dispatched with a release tag, that verifies that tag's *published* artifacts on ubuntu, macos and windows: both documented installers install the archive, the installed binary reports that version and starts, and the extension package the same tag published is installed by id from the feed a user reads, listed by `lunate packages`, verified with `--verify`, discovered by `lunate extensions` as pending trust, and uninstalled with neither content nor record left behind. The workflow SHALL keep the install in a state root outside the checkout, SHALL run keyless, and SHALL fail by name when the tag published no such package version. Before it installs the package, the workflow SHALL wait for the feed to list that version — polling the flatcontainer index, the package content and the registration resource under one budget of about ten minutes — and SHALL install once, against a feed it has just seen serve the version. When the budget runs out it SHALL fail naming the version it waited for and the state of each resource, rather than retrying a fixed number of times.

#### Scenario: A published tag passes the smoke

- **GIVEN** a release tag whose archive and extension package were published
- **WHEN** `smoke.yml` runs for that tag
- **THEN** all three operating systems install the archive, run the binary, and complete install → verify → uninstall of the package

#### Scenario: A tag without the package fails by name

- **GIVEN** a release tag cut before the extension package existed
- **WHEN** `smoke.yml` runs for that tag
- **THEN** the package step fails naming the package and the version it asked the feed for

#### Scenario: The smoke cannot be made to pass by a local store

- **GIVEN** a runner whose home already holds a Lunate store
- **WHEN** `smoke.yml` runs
- **THEN** every package command reads and writes a state root under the runner's temporary directory, and the pre-existing store is neither read nor written

#### Scenario: A freshly pushed version is waited for, not retried

- **GIVEN** a tag whose package was pushed minutes ago and is not yet indexed by the feed
- **WHEN** the smoke reaches the extension step
- **THEN** it waits until the feed lists the version in the flatcontainer index, serves its `.nupkg`, and lists it in the registration resource, and then installs it once

#### Scenario: An exhausted budget names the feed state

- **GIVEN** a tag whose package version does not become visible within the budget
- **WHEN** the wait ends
- **THEN** the step fails naming the package and version and what each resource served — absent from the index, content not downloadable, registration not served, or unreachable — and the install is not attempted

#### Scenario: The wait is proven without the network

- **GIVEN** fixture feeds served from the filesystem
- **WHEN** the gate's self-tests run
- **THEN** the wait succeeds for a version that appears in the middle of the budget, fails when the version never appears, and refuses a version whose content is missing
