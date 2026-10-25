## MODIFIED Requirements

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
