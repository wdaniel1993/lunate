## ADDED Requirements

### Requirement: The gate has two stages

The repository SHALL define two gates in `AGENTS.md`: a fast gate for iteration (`dotnet csharpier check .`, `openspec validate <change> --strict`, a Release build and the test classes covering the change) and the full gate (`scripts/verify.sh`, `scripts/verify.ps1` on Windows). The fast gate SHALL be the condition for committing and pushing during a change; the full gate SHALL be the condition for marking a change ready for review and for calling it done; `ci-ok` SHALL be green before a merge. The full gate's contents SHALL NOT change with this definition.

#### Scenario: An intermediate commit

- **WHEN** an agent has a compiling change with its focused tests green and CSharpier clean
- **THEN** it may commit and push without running the full gate

#### Scenario: Asking for review

- **WHEN** a change is proposed for review
- **THEN** `scripts/verify.sh` has passed on the head being reviewed

#### Scenario: Merging

- **WHEN** a pull request is merged
- **THEN** the `ci-ok` check is green
