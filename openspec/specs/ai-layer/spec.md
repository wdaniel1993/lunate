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

### Requirement: Layer reuse and retry boundary
`Lunate.Ai` SHALL reuse `UseLogging` and `UseOpenTelemetry` (opt-in) instead of bespoke middleware, SHALL use `AIJsonUtilities` options for serializing Microsoft.Extensions.AI types where serialization exists, and SHALL NOT implement retries — the loop owns them and surfaces them as events.

#### Scenario: Retries are absent from the layer
- **GIVEN** the factory pipeline
- **WHEN** the provider throws a transient error
- **THEN** the exception surfaces unchanged (no retry inside `Lunate.Ai`)

### Requirement: Recording and replay of provider streams
`Lunate.Ai` SHALL record provider exchanges as JSONL when `LUNATE_RECORD=1` for both call styles: a streamed exchange (`GetStreamingResponseAsync`) records its update sequence, and an aggregated exchange (`GetResponseAsync`) records the response as its update sequence so replay serves both call styles from one exchange. `Lunate.Ai` SHALL replay fixtures deterministically: `ReplayChatClient` answers requests from the recorded exchanges in order, verifying each request digest, with actionable errors on mismatch or exhaustion. Fixtures SHALL serialize Microsoft.Extensions.AI types with `AIJsonUtilities` options and replay SHALL NOT require API keys or network access. When `LUNATE_REPLAY` names a fixture path, the factory's standard resolution path SHALL wire `ReplayChatClient` for it without resolving a credential or constructing a provider client, so a session replays with no key and no network; the explicit-credential overload (`Create(model, apiKey)`) SHALL NOT replay, and an explicit recording request takes precedence when both are set. When `LUNATE_RECORD=1` is set without an explicit path, recordings SHALL default under the user's home directory (`~/.lunate/recordings/`), never inside the current working directory. The request digest SHALL cover the messages, the model id and the tool names; tool descriptions and schemas are deliberately outside the digest, so presentation changes do not invalidate a fixture (revisit if it bites). Scripted fixtures SHALL be marked with the model id `synthetic`; live recordings carry the real model id.

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

#### Scenario: Factory wires the replay client

- **GIVEN** `LUNATE_REPLAY` set to a committed fixture path
- **WHEN** the factory builds a pipeline
- **THEN** the pipeline serves the fixture deterministically without resolving a credential or constructing a provider client

#### Scenario: Recording wins over replay

- **GIVEN** `LUNATE_RECORD=1` and `LUNATE_REPLAY` both set
- **WHEN** the factory builds a pipeline
- **THEN** the recorder is wired, never the replay client

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

The factory SHALL send a provider's environment API key only to the provider's default endpoint: when a model declares a custom endpoint without an explicit credential reference, both the OpenAI-compatible and the Anthropic adapter SHALL construct their clients with a placeholder credential instead of the environment key, and SHALL NOT fail for a missing key — so local servers work without dummy environment variables and real keys are never sent to third-party endpoints. A model that spells out the provider's own default URL counts as a custom endpoint. A model MAY declare an explicit credential reference (`"auth": "<name>"` in `models.json`), the deliberate opt-in that resolves that named key from the user's auth store and sends it to the declared endpoint; a reference that cannot be resolved SHALL fail with an error naming the key. For models without a reference: default endpoints resolve the provider credential from the environment first, then the auth store, and fail with an actionable error when neither yields a key.

#### Scenario: Custom endpoint without a key

- **GIVEN** a model with a custom endpoint and no `OPENAI_API_KEY`
- **WHEN** the factory builds the pipeline
- **THEN** no error is raised and requests target the custom endpoint

#### Scenario: Custom endpoint never receives the environment key

- **GIVEN** a model with a custom endpoint and `OPENAI_API_KEY` set
- **WHEN** the factory builds the pipeline
- **THEN** the client uses the placeholder credential, not the environment key

#### Scenario: Explicit reference sends the named key

- **GIVEN** a model with a custom endpoint and `"auth": "work"` resolving to a stored key
- **WHEN** the factory builds the pipeline
- **THEN** the client is constructed with that key for the declared endpoint

#### Scenario: Unresolvable reference names the key

- **GIVEN** a model with `"auth": "missing"` and no such stored key
- **WHEN** the factory builds the pipeline
- **THEN** the error names `missing` and mentions auth.json, and no secret value appears

#### Scenario: The Anthropic adapter is symmetric

- **GIVEN** an Anthropic model with a custom endpoint and no `ANTHROPIC_API_KEY`
- **WHEN** the factory builds the pipeline
- **THEN** no error is raised and the client uses the placeholder credential

#### Scenario: Default endpoint requires the environment key

- **GIVEN** a model without a custom endpoint and no key in the environment or auth store
- **WHEN** the factory builds the pipeline
- **THEN** it fails with an actionable error naming the variable and auth.json

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

### Requirement: Anthropic prompt-cache breakpoints

For a model whose provider is `anthropic`, `Lunate.Ai` SHALL place prompt-cache breakpoints (`cache_control: {"type":"ephemeral"}`) on the last system block, the last tool declaration, and the last content block of the latest message. The latest-message breakpoint SHALL roll forward on every turn; the system and tool breakpoints SHALL stay on the same blocks. No request SHALL carry more than four breakpoints. The marker client SHALL count every `cache_control` marker present on the outgoing request — its own three plus any pre-existing markers on content blocks or tool declarations — and SHALL throw `InvalidOperationException` in all builds, before the request is sent, when the total exceeds four. For a model whose provider is not `anthropic` (OpenAI included) `Lunate.Ai` SHALL add no cache markers — those providers cache automatically.

Markers SHALL be request-local: the client SHALL NOT mutate the session history, the prompt sections or the registered tool declarations. The system prompt, tool declarations and unchanged prompt sections SHALL therefore lead consecutive requests byte-identically. The existing thinking parameters (`RawRepresentationFactory` for `budget` models) SHALL compose unchanged, and the adapter SHALL continue to build the wire messages from the `ChatMessage`s so signed thinking blocks and tool results round-trip exactly as before.

#### Scenario: Three breakpoints reach the wire

- **GIVEN** an Anthropic model, a system prompt, one tool and a two-message history
- **WHEN** a request passes through the factory and the real Anthropic adapter to a capturing handler
- **THEN** the JSON body carries `cache_control` on the last system block, on the last tool, and on the last content block of the latest message, and nowhere else

#### Scenario: The rolling breakpoint moves

- **GIVEN** two consecutive requests built from the same system prompt, tools and growing history
- **WHEN** both request bodies are captured
- **THEN** the system and tool bytes are identical, turn one's latest-message breakpoint is not repeated on turn two, and turn two carries its breakpoint on its own last content block

#### Scenario: At most four breakpoints

- **GIVEN** a request with several system blocks and several tools
- **WHEN** the marker client prepares it
- **THEN** the body contains at most four `cache_control` markers

#### Scenario: An over-limit request fails loudly

- **GIVEN** a request whose history already carries markers and whose system, tool and latest-message markers bring the total above four
- **WHEN** the marker client prepares it
- **THEN** it throws `InvalidOperationException` in every build and nothing reaches the provider

#### Scenario: OpenAI gets no markers

- **GIVEN** an OpenAI model
- **WHEN** a request is prepared
- **THEN** no `cache_control` field appears in the request and no marker client is inserted into the OpenAI pipeline

### Requirement: Cache-stable system prefix

The core system prompt SHALL be composed once per harness build and SHALL NOT be recomposed between turns of one harness. Consecutive turns of one harness SHALL send a byte-identical system message, tool list and unchanged prompt sections; only the rolling latest-message breakpoint and the appended history may differ. A wholesale reload (`/new`, resume, `/model`) MAY recompose the prompt, and an extension `RunStarting` edit MAY intentionally replace a section — both invalidate the cache by design, never silently mid-turn.

#### Scenario: Two turns keep the same prefix

- **GIVEN** a harness with a system prompt and a scripted client that records each request
- **WHEN** two consecutive runs execute without a reload
- **THEN** both requests carry the same system message bytes and the same tool declarations

#### Scenario: The date does not drift between turns

- **GIVEN** a harness composed on one date and run across a simulated date change
- **WHEN** the second run's request is inspected
- **THEN** its system prompt carries the date captured when the harness was built
