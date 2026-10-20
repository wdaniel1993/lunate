# print-mode — delta for prompt-caching

## ADDED Requirements

### Requirement: Cache usage in JSON output

With `--json`, a `usage_updated` object SHALL carry the provider's prompt-cache counts alongside the existing `usage` object: `cacheReadTokens` from `UsageDetails.CachedInputTokenCount` and `cacheWriteTokens` from `UsageDetails.AdditionalCounts["CacheCreationInputTokens"]`, each emitted only when the provider reported a positive count. A reported zero SHALL be treated as unknown and omitted, and the two counts SHALL never be synthetic zeros. The `usage` object itself SHALL keep its existing shape.

#### Scenario: A usage event carries both counts

- **GIVEN** a run whose provider reports `CachedInputTokenCount = 1200` and a cache creation count of `300`
- **WHEN** print mode runs with `--json`
- **THEN** its `usage_updated` line has `cacheReadTokens: 1200` and `cacheWriteTokens: 300` next to the unchanged `usage` object

#### Scenario: Unknown counts are absent

- **GIVEN** a provider that reports only input and output tokens
- **WHEN** print mode runs with `--json`
- **THEN** the `usage_updated` line has neither cache field

#### Scenario: A reported zero is treated as unknown

- **GIVEN** a provider that reports `CachedInputTokenCount = 0` and a cache creation count of `0`
- **WHEN** print mode runs with `--json`
- **THEN** the `usage_updated` line has neither cache field, while the nested raw `usage` object still carries the provider's `cachedInputTokenCount: 0`

#### Scenario: The default output is unchanged

- **GIVEN** print mode without `--json`
- **WHEN** a run reports cache counts
- **THEN** stdout still carries only the final assistant text
