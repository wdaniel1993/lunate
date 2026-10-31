## ADDED Requirements

### Requirement: ADR number verification
The documentation verification gate SHALL check primary ADR filename numbers for uniqueness and density, using filenames only. The check SHALL run in the `verify-docs` profile without invoking a build or test suite. Files named `NNNN-addendum-*.md` SHALL be treated as addenda to their parent ADR, not as separate primary ADR numbers. On a duplicate or gap, the diagnostic SHALL identify both relevant files, state any missing number, and report the next free number (one greater than the highest primary ADR number). The author-facing numbering rule SHALL be documented in `docs/guide.md` because the repository has no ADR README.

#### Scenario: Duplicate primary numbers fail with actionable paths
- **GIVEN** two primary ADR files claim the same number
- **WHEN** the ADR numbering check runs
- **THEN** it fails and names both files and the next free number

#### Scenario: A gap in primary numbers fails with actionable paths
- **GIVEN** primary ADR files `0001-first.md` and `0003-third.md` with no `0002` file
- **WHEN** the ADR numbering check runs
- **THEN** it fails, names both gap-bracketing files, identifies `0002` as missing, and reports `0004` as the next free number

#### Scenario: A dense primary sequence passes
- **GIVEN** primary ADR files numbered consecutively from `0001` with no duplicate
- **WHEN** the ADR numbering check runs
- **THEN** it passes

#### Scenario: Numbered addenda do not create duplicate primary numbers
- **GIVEN** an ADR and one or more matching `NNNN-addendum-*.md` files
- **WHEN** the ADR numbering check runs
- **THEN** the addenda are excluded from primary-number uniqueness and density checks

#### Scenario: The check runs in the docs-only profile
- **GIVEN** the repository verification gate runs with `--profile docs`
- **WHEN** it reaches the documentation checks
- **THEN** ADR numbering is checked without a build or test suite
