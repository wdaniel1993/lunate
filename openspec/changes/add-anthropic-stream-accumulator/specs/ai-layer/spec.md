## ADDED Requirements

### Requirement: Anthropic provider adapter
`Lunate.Ai` SHALL provide an `IChatClient` implementation for Anthropic, built on the official Anthropic .NET package, selected by `ModelInfo.Provider == "anthropic"`, authenticated from `ANTHROPIC_API_KEY`, mapping messages, tools and streaming to Microsoft.Extensions.AI types.

#### Scenario: Provider selection
- **GIVEN** a model with provider "anthropic"
- **WHEN** the factory builds the pipeline
- **THEN** the Anthropic adapter serves it, and an unknown provider still fails with an actionable error naming the supported providers

#### Scenario: Streaming maps to MEAI types
- **GIVEN** scripted SDK stream events
- **WHEN** the adapter streams them
- **THEN** the consumer receives `ChatResponseUpdate` values with the same content (text, tool calls, usage)

### Requirement: Complete function calls at the consumer
The pipeline SHALL present function calls only in complete form: a `StreamAccumulator` above the recorder assembles streamed argument fragments per call id, so consumers never act on partial calls. Recordings SHALL remain raw provider output.

#### Scenario: Fragmented arguments are assembled
- **GIVEN** a stream that delivers one function call's arguments in fragments
- **WHEN** it passes the accumulator
- **THEN** the consumer sees one complete call with the full arguments

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
