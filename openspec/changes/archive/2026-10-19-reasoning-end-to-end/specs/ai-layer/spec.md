# ai-layer Specification

## MODIFIED Requirements

### Requirement: Model catalog merge
`ModelCatalog` SHALL merge the embedded `models.json` with `~/.lunate/models.json`; user entries SHALL override built-ins by id; adding a model SHALL never require a code change. A model MAY declare its reasoning style with `reasoning`: `"effort"` (OpenAI-compatible reasoning effort), `"budget"` (Anthropic extended thinking) or `"inline"` (the model embeds ` thinking…</think>` in its text stream); a model without the field declares no reasoning support and behaves exactly as before.

#### Scenario: User override wins
- **GIVEN** a built-in model and a user file redefining the same id
- **WHEN** the catalog is loaded
- **THEN** the user definition is used

#### Scenario: New model without code changes
- **GIVEN** a user `models.json` with a new id
- **WHEN** the catalog is loaded
- **THEN** the model is available

#### Scenario: Duplicate id within one file fails
- **GIVEN** a `models.json` containing the same id twice
- **WHEN** the catalog is loaded
- **THEN** loading fails with an error naming the id

#### Scenario: A model declaring its reasoning style

- **GIVEN** a `models.json` entry with `"reasoning": "effort"`
- **WHEN** the catalog is loaded
- **THEN** the model's reasoning style is available to the factory, and a model without the field reports none

### Requirement: Anthropic provider adapter
`Lunate.Ai` SHALL provide Anthropic support through the official Anthropic .NET package and its first-party Microsoft.Extensions.AI adapter, selected by `ModelInfo.Provider == "anthropic"` and authenticated from `ANTHROPIC_API_KEY`. The factory SHALL construct it with transport retries disabled so retries remain exclusively the loop's. When the model's reasoning style is `budget` and the session's thinking level is on, the factory SHALL enable extended thinking with the level's token budget and ensure the request carries `max_tokens` (clamped above the budget); signed thinking blocks returned by the model SHALL be round-tripped unchanged within the model's own tool-use loops, and this round-trip SHALL be covered by the live contract test.

#### Scenario: Provider selection
- **GIVEN** a model with provider "anthropic"
- **WHEN** the factory builds the pipeline
- **THEN** the Anthropic client serves it, and an unknown provider still fails with an actionable error naming the supported providers

#### Scenario: Contract coverage
- **GIVEN** `LUNATE_LIVE=1` and `ANTHROPIC_API_KEY` set
- **WHEN** the contract test runs a streamed round-trip
- **THEN** the consumer receives Microsoft.Extensions.AI updates (text, tool calls, usage); without the flag the test is skipped

#### Scenario: Thinking round-trips through a tool loop

- **GIVEN** `LUNATE_LIVE=1`, `ANTHROPIC_API_KEY` set and a model with reasoning style `budget`
- **WHEN** a streamed round-trip carries signed thinking blocks through a tool-use loop
- **THEN** the consumer receives the reasoning content and the follow-up request returns the blocks unchanged without an API rejection

## ADDED Requirements

### Requirement: Reasoning options from the session level

The factory SHALL translate the session's thinking level (`off` | `low` | `medium` | `high`) into provider options per the model's declared reasoning style: `effort` maps to the OpenAI-compatible reasoning effort (low/medium/high), `budget` maps to an Anthropic thinking budget (2,048 / 8,192 / 32,768 tokens), and `inline` sends no provider parameter. A level of `off` — and any model without a declared style — SHALL send no reasoning parameters at all. The level-to-value mapping SHALL be culture-invariant and covered by tests per style.

#### Scenario: Off sends nothing

- **GIVEN** any reasoning style
- **WHEN** the thinking level is `off`
- **THEN** the request carries no reasoning parameters

#### Scenario: Effort maps the level

- **GIVEN** a model with style `effort`
- **WHEN** the level is `medium`
- **THEN** the request carries the medium reasoning effort

#### Scenario: Budget maps the level

- **GIVEN** a model with style `budget`
- **WHEN** the level is `high`
- **THEN** the request carries a 32,768-token thinking budget, clamped below the request's max tokens

#### Scenario: Inline sends no parameter

- **GIVEN** a model with style `inline`
- **WHEN** the level is `high`
- **THEN** the request carries no reasoning parameters and the inline splitter is active

### Requirement: Reasoning content is provider-scoped

Reasoning content SHALL round-trip only to the provider that produced it: a request SHALL carry `TextReasoningContent` from history entries whose recorded model id equals the current call's model id — signed thinking blocks require being returned unchanged within a model's own tool-use loops — and SHALL strip reasoning content from entries recorded under any other model id. Stripping SHALL apply to the request copy only; the session file SHALL keep the content. Assistant reasoning content (including provider `ProtectedData`) SHALL persist inside the embedded `ChatMessage`, byte-identically re-serializable, with no schema change.

#### Scenario: Same-model loops keep the blocks

- **GIVEN** a history with reasoning content recorded under the current model
- **WHEN** a request is built
- **THEN** the reasoning content is included unchanged

#### Scenario: Cross-model requests strip

- **GIVEN** a history with reasoning content recorded under a different model id
- **WHEN** a request is built for the current model
- **THEN** the reasoning content is absent from the request and still present in the session file

#### Scenario: A reasoning session round-trips byte-identically

- **GIVEN** a session entry whose message carries reasoning content with `ProtectedData`
- **WHEN** the session is loaded and re-serialized
- **THEN** the bytes are identical
