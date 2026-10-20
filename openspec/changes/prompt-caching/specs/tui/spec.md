# tui — delta for prompt-caching

## ADDED Requirements

### Requirement: Cache usage in `/context`

The `/context` notice SHALL show the session's accumulated prompt-cache tokens when the provider has reported them: cache reads and cache writes as separate counts, formatted culture-invariantly, in a `cache: read <n>, write <m>` segment. A count the provider never reported SHALL be omitted from the segment; the command SHALL NOT display a zero for an unknown count. The footer line itself SHALL remain unchanged — cache counts belong to the explicit `/context` readout, not to the always-visible status.

#### Scenario: Cache read and write are shown

- **GIVEN** usage events carrying `CachedInputTokenCount = 1200` and `AdditionalCounts["CacheCreationInputTokens"] = 300`
- **WHEN** `/context` runs
- **THEN** the notice contains `cache: read 1.2k, write 300`

#### Scenario: An unknown count is omitted

- **GIVEN** a provider that reports input and output tokens but no cache counts
- **WHEN** `/context` runs
- **THEN** the notice has no cache segment and no invented zeros

#### Scenario: The cache segment is separate from the budget total

- **GIVEN** usage whose `InputTokenCount` already includes the cached tokens
- **WHEN** the session accumulates cache reads and writes
- **THEN** the token total is unchanged and the cache counts are display-only
