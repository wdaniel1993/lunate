# TUI Specification Delta

## ADDED Requirements

### Requirement: Chat profile selection

`/model` SHALL select or switch the profile assigned to `chat` and SHALL leave the `fast`, `decision`, `subagents`, and `compaction` usage assignments unchanged. The picker SHALL identify the active chat profile and may list model profiles and catalog models. An explicit `provider/model` selection SHALL use that provider's default settings. A bare model id SHALL be accepted only when exactly one provider/profile candidate matches; otherwise the notice SHALL refuse it and name every candidate. The session model-change record SHALL identify the resulting chat profile and resolved model.

#### Scenario: `/model` switches chat profile only

- **GIVEN** configured profiles for chat, fast, decision, subagents, and compaction
- **WHEN** `/model` switches to another chat profile
- **THEN** the chat profile changes and the other usage assignments remain unchanged

#### Scenario: An ad-hoc qualified model uses provider defaults

- **GIVEN** a provider with default settings and a catalog model addressable as `provider-a/model-x`
- **WHEN** `/model provider-a/model-x` is submitted
- **THEN** chat uses that model with the provider's default settings

#### Scenario: An ambiguous bare model id names its candidates

- **GIVEN** two provider profiles expose model id `model-x`
- **WHEN** `/model model-x` is submitted
- **THEN** no switch occurs and the notice names both qualified candidates

### Requirement: Cost command groups usage by provider

The interactive session SHALL provide `/cost` as a read-only command with no model call. It SHALL report priced usage grouped by usage and provider, include the model ids contributing to each group, and show a total consistent with the priced groups. Unknown-priced lanes SHALL be marked unpriced rather than shown as zero. Technical values SHALL use culture-invariant formatting.

#### Scenario: `/cost` reports usage and provider splits

- **GIVEN** recorded priced usage from chat, fast, subagents, and compaction across two providers
- **WHEN** `/cost` is submitted
- **THEN** the output groups amounts by usage and by provider, identifies contributing model ids, and includes no model-call result

#### Scenario: Unknown pricing is explicit

- **GIVEN** a recorded usage with an unknown rate for a positive token lane
- **WHEN** `/cost` is submitted
- **THEN** that usage is marked unpriced and is not displayed as `$0`
