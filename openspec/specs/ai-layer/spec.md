# ai-layer Specification

## Purpose
Model access for Lunate: the factory that builds the correctly ordered `IChatClient` pipeline (OpenTelemetry → logging → recorder → provider), the model catalog that merges built-in and user definitions without code changes, and the layer rules that keep tool invocation and retries out of model access.

## Requirements

### Requirement: Chat-client factory pipeline order
`IChatClientFactory.Create` SHALL build the pipeline in the fixed order, outermost first: OpenTelemetry (opt-in) → logging → recorder slot → provider. Replay will replace recorder + provider so tests still pass through the telemetry and logging layers.

#### Scenario: Pipeline order is fixed
- **GIVEN** a factory configured with a stub provider client
- **WHEN** a pipeline is created and a request flows through it
- **THEN** the telemetry, logging, recorder and provider layers observe the request in exactly that order

#### Scenario: FunctionInvokingChatClient is never used
- **GIVEN** any factory configuration
- **WHEN** the pipeline is built
- **THEN** no `FunctionInvokingChatClient` or `AIFunctionFactory` appears anywhere in it

### Requirement: Model catalog merge
`ModelCatalog` SHALL merge the embedded `models.json` with `~/.lunate/models.json`; user entries SHALL override built-ins by id; adding a model SHALL never require a code change.

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

### Requirement: Layer reuse and retry boundary
`Lunate.Ai` SHALL reuse `UseLogging` and `UseOpenTelemetry` (opt-in) instead of bespoke middleware, SHALL use `AIJsonUtilities` options for serializing Microsoft.Extensions.AI types where serialization exists, and SHALL NOT implement retries — the loop owns them and surfaces them as events.

#### Scenario: Retries are absent from the layer
- **GIVEN** the factory pipeline
- **WHEN** the provider throws a transient error
- **THEN** the exception surfaces unchanged (no retry inside `Lunate.Ai`)

### Requirement: Recording and replay of provider streams
`Lunate.Ai` SHALL record provider exchanges as JSONL when `LUNATE_RECORD=1` for both call styles: a streamed exchange (`GetStreamingResponseAsync`) records its update sequence, and an aggregated exchange (`GetResponseAsync`) records the response as its update sequence so replay serves both call styles from one exchange. `Lunate.Ai` SHALL replay fixtures deterministically: `ReplayChatClient` answers requests from the recorded exchanges in order, verifying each request digest, with actionable errors on mismatch or exhaustion. Fixtures SHALL serialize Microsoft.Extensions.AI types with `AIJsonUtilities` options and replay SHALL NOT require API keys or network access.

#### Scenario: Record then replay is identical
- **GIVEN** a provider stream recorded to a fixture
- **WHEN** `ReplayChatClient` serves the same request
- **THEN** the replayed updates are identical to the recorded ones

#### Scenario: Aggregated exchange record then replay is identical
- **GIVEN** a provider response recorded via `GetResponseAsync`
- **WHEN** `ReplayChatClient` serves the same request
- **THEN** the replayed aggregated response matches the recorded response

#### Scenario: Request mismatch fails actionably
- **GIVEN** a fixture whose next exchange has digest A
- **WHEN** a request with digest B arrives
- **THEN** replay fails with an error naming the fixture, the exchange index and the digests

#### Scenario: Replay needs no API keys or network
- **GIVEN** any committed fixture
- **WHEN** it is replayed
- **THEN** no network call and no API key is required

#### Scenario: Factory wires the recorder
- **GIVEN** `LUNATE_RECORD=1` with a recording path
- **WHEN** the factory builds a pipeline
- **THEN** a recorder writes each exchange to the path while the stream passes through unchanged
