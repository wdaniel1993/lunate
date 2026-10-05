## ADDED Requirements

### Requirement: Anthropic provider adapter
`Lunate.Ai` SHALL provide Anthropic support through the official Anthropic .NET package and its first-party Microsoft.Extensions.AI adapter, selected by `ModelInfo.Provider == "anthropic"` and authenticated from `ANTHROPIC_API_KEY`. The factory SHALL construct it with transport retries disabled so retries remain exclusively the loop's.

#### Scenario: Provider selection
- **GIVEN** a model with provider "anthropic"
- **WHEN** the factory builds the pipeline
- **THEN** the Anthropic client serves it, and an unknown provider still fails with an actionable error naming the supported providers

#### Scenario: Contract coverage
- **GIVEN** `LUNATE_LIVE=1` and `ANTHROPIC_API_KEY` set
- **WHEN** the contract test runs a streamed round-trip
- **THEN** the consumer receives Microsoft.Extensions.AI updates (text, tool calls, usage); without the flag the test is skipped

### Requirement: Complete function calls at the consumer
The pipeline SHALL present function calls only in complete form: a `StreamAccumulator` above the recorder assembles streamed argument fragments per call id, so consumers never act on partial calls. Recordings SHALL remain raw provider output.

#### Scenario: Fragmented arguments are assembled
- **GIVEN** a stream that delivers one function call's arguments in fragments
- **WHEN** it passes the accumulator
- **THEN** the consumer sees one complete call with the full arguments

#### Scenario: Already-complete calls pass unchanged
- **GIVEN** a stream whose function calls are already complete
- **WHEN** it passes the accumulator
- **THEN** the updates pass through unchanged

#### Scenario: Recordings stay raw
- **GIVEN** recording is enabled
- **WHEN** a fragmented stream is recorded
- **THEN** the fixture contains the raw fragments, and replay through the pipeline still assembles them

### Requirement: Local endpoints without a dummy key
When a model declares a custom endpoint and no API key is configured, the OpenAI-compatible adapter SHALL construct its client with a placeholder credential instead of failing, so local servers work without dummy environment variables.

#### Scenario: Custom endpoint without a key
- **GIVEN** a model with a custom endpoint and no `OPENAI_API_KEY`
- **WHEN** the factory builds the pipeline
- **THEN** no error is raised and requests target the custom endpoint
