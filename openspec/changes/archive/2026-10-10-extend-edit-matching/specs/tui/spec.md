## MODIFIED Requirements

### Requirement: Diff rendering with match tier

Edit and write blocks SHALL render their unified diff as a red/green borderless panel — deletions red, insertions green, headers and context dimmed — with the match tier shown as a label, and a fallback match (`normalized` or `indent`) visibly flagged so no fallback is silent.

#### Scenario: Both tiers render distinctly

- **GIVEN** an edit that matched exactly and one that matched via normalization
- **WHEN** their blocks render
- **THEN** the diff is red/green in both and the tier label distinguishes exact from normalized (the latter flagged)

#### Scenario: Malformed or empty diffs never break rendering

- **GIVEN** a diff string that is empty or malformed
- **WHEN** the block renders
- **THEN** it renders without the panel (or with the panel omitted) and never throws

#### Scenario: An indent fallback renders flagged

- **GIVEN** an edit that matched via the indent tier
- **WHEN** its block renders
- **THEN** the tier label says indent and the fallback is flagged like normalized
