# print-mode — delta for prompt-caching

## ADDED Requirements

### Requirement: Cache usage in JSON output

With `--json`, a `usage_updated` object SHALL carry the provider's prompt-cache counts alongside the existing `usage` object: `cacheReadTokens` from `UsageDetails.CachedInputTokenCount` and `cacheWriteTokens` from `UsageDetails.AdditionalCounts["CacheCreationInputTokens"]`, each emitted only when the provider reported it. The `usage` object itself SHALL keep its existing shape. The two counts SHALL be integers formatted through the JSON serializer, never synthetic zeros.

#### Scenario: A usage event carries both counts

- **GIVEN** a run whose provider reports `CachedInputTokenCount = 1200` and a cache creation count of `300`
- **WHEN** print mode runs with `--json`
- **THEN** its `usage_updated` line has `cacheReadTokens: 1200` and `cacheWriteTokens: 300` next to the unchanged `usage` object

#### Scenario: Unknown counts are absent

- **GIVEN** a provider that reports only input and output tokens
- **WHEN** print mode runs with `--json`
- **THEN** the `usage_updated` line has neither cache field

#### Scenario: The default output is unchanged

- **GIVEN** print mode without `--json`
- **WHEN** a run reports cache counts
- **THEN** stdout still carries only the final assistant text
