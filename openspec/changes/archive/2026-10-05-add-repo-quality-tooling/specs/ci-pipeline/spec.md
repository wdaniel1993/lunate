## ADDED Requirements

### Requirement: Code scanning
A CodeQL workflow SHALL analyze the C# code on pushes to `main`, pull requests to `main` and a weekly schedule, uploading results to GitHub code scanning.

#### Scenario: Analysis on a clean change
- **GIVEN** a pull request that satisfies the gate
- **WHEN** the CodeQL workflow runs
- **THEN** the analysis completes green and results are uploaded to code scanning
