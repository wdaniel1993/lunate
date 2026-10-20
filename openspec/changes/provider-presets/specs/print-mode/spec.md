## ADDED Requirements

### Requirement: Cost in JSON output

With `--json`, a `usage_updated` object SHALL carry a numeric `costUsd` when the record's model is in the catalog and every token lane with a positive count has a known price. The value SHALL be the record's cost in US dollars, computed with `decimal` and invariant arithmetic. A record whose model is unknown or unpriced SHALL omit the field rather than emit `0`; a zero-token priced record MAY carry `0`. The existing `usage` object and the cache fields SHALL keep their shape. The field SHALL be absent from the non-JSON output, which still carries only the final answer on stdout.

#### Scenario: A priced record carries its cost

- **GIVEN** a `usage_updated` record for a priced model with input, output and cache tokens
- **WHEN** `--json` serializes it
- **THEN** the line carries `costUsd` beside the unchanged `usage` object

#### Scenario: An unpriced model omits the field

- **GIVEN** a `usage_updated` record whose model has no pricing
- **WHEN** `--json` serializes it
- **THEN** the line has no `costUsd` field

#### Scenario: An unknown cache rate suppresses a used lane's cost

- **GIVEN** a record with positive cache-read tokens and no cache-read rate
- **WHEN** `--json` serializes it
- **THEN** the line has no `costUsd` field

#### Scenario: The default output is unchanged

- **GIVEN** print mode without `--json`
- **WHEN** a run reports priced usage
- **THEN** stdout still carries only the final assistant text and no cost field
