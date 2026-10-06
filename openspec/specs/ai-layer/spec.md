# ai-layer Specification

## Purpose
Model access for Lunate: the factory that builds the correctly ordered `IChatClient` pipeline (OpenTelemetry → logging → accumulator → recorder → provider), the model catalog that merges built-in and user definitions without code changes, and the layer rules that keep tool invocation and retries out of model access.

## Requirements

### Requirement: Chat-client factory pipeline order
`IChatClientFactory.Create` SHALL build the pipeline in the fixed order, outermost first: OpenTelemetry (opt-in) → logging → accumulator → recorder slot → provider. Replay will replace recorder + provider so tests still pass through the telemetry and logging layers.

#### Scenario: Pipeline order is fixed
- **GIVEN** a factory configured with a stub provider client
- **WHEN** a pipeline is created and a request flows through it
- **THEN** the telemetry, logging, accumulator, recorder and provider layers observe the request in exactly that order

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
`Lunate.Ai` SHALL record provider exchanges as JSONL when `LUNATE_RECORD=1` for both call styles: a streamed exchange (`GetStreamingResponseAsync`) records its update sequence, and an aggregated exchange (`GetResponseAsync`) records the response as its update sequence so replay serves both call styles from one exchange. `Lunate.Ai` SHALL replay fixtures deterministically: `ReplayChatClient` answers requests from the recorded exchanges in order, verifying each request digest, with actionable errors on mismatch or exhaustion. Fixtures SHALL serialize Microsoft.Extensions.AI types with `AIJsonUtilities` options and replay SHALL NOT require API keys or network access. When `LUNATE_RECORD=1` is set without an explicit path, recordings SHALL default under the user's home directory (`~/.lunate/recordings/`), never inside the current working directory. The request digest SHALL cover the messages, the model id and the tool names; tool descriptions and schemas are deliberately outside the digest, so presentation changes do not invalidate a fixture (revisit if it bites). Scripted fixtures SHALL be marked with the model id `synthetic`; live recordings carry the real model id.

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

#### Scenario: Default recordings are user-scoped
- **GIVEN** `LUNATE_RECORD=1` without `LUNATE_RECORD_PATH`
- **WHEN** the factory wires the recorder
- **THEN** the recording path is under `~/.lunate/recordings/`, not inside the current working directory

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
The pipeline SHALL present function calls only in complete form: a `StreamAccumulator` above the recorder assembles streamed argument fragments per call id, so consumers never act on partial calls. A fragment is a `FunctionCallContent` whose `Arguments` contains exactly one entry under the reserved `$arguments` key holding a raw JSON fragment (a string, or a `JsonElement` of string kind); fragments merge by call id and are emitted as one parsed call in a synthesized update at stream end. When assembly fails, the synthesized call SHALL keep the concatenated raw fragment text under the reserved `$arguments` key and SHALL set its `Exception`. Recordings SHALL remain raw provider output.

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

#### Scenario: Failed assembly preserves the raw text
- **GIVEN** a stream whose concatenated function-call arguments are not valid JSON
- **WHEN** it passes the accumulator
- **THEN** the emitted call carries the raw fragment text under the reserved `$arguments` key and its `Exception` is set

### Requirement: Local endpoints without a dummy key
The factory SHALL send a provider's environment API key only to the provider's default endpoint: when a model declares a custom endpoint, both the OpenAI-compatible and the Anthropic adapter SHALL construct their clients with a placeholder credential instead of the environment key, and SHALL NOT fail for a missing key — so local servers work without dummy environment variables and real keys are never sent to third-party endpoints. A model that spells out the provider's own default URL counts as a custom endpoint. Explicit per-model key references arrive with the settings work (T-16).

#### Scenario: Custom endpoint without a key
- **GIVEN** a model with a custom endpoint and no `OPENAI_API_KEY`
- **WHEN** the factory builds the pipeline
- **THEN** no error is raised and requests target the custom endpoint

#### Scenario: Custom endpoint never receives the environment key
- **GIVEN** a model with a custom endpoint and `OPENAI_API_KEY` set
- **WHEN** the factory builds the pipeline
- **THEN** the client uses the placeholder credential, not the environment key

#### Scenario: The Anthropic adapter is symmetric
- **GIVEN** an Anthropic model with a custom endpoint and no `ANTHROPIC_API_KEY`
- **WHEN** the factory builds the pipeline
- **THEN** no error is raised and the client uses the placeholder credential

#### Scenario: Default endpoint requires the environment key
- **GIVEN** a model without a custom endpoint and no key in the environment
- **WHEN** the factory builds the pipeline
- **THEN** it fails with an actionable error naming the variable and T-16
