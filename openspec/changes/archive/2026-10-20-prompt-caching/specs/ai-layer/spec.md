# ai-layer — delta for prompt-caching

## ADDED Requirements

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
