## MODIFIED Requirements

### Requirement: Model catalog merge
`ModelCatalog` SHALL merge the embedded `models.json` with `~/.lunate/models.json`; user entries SHALL override built-ins by id; adding a model SHALL never require a code change. A model MAY declare its reasoning style with `reasoning`: `"effort"` (OpenAI-compatible reasoning effort), `"budget"` (Anthropic extended thinking) or `"inline"` (the model embeds ` thinking…</think>` in its text stream); a model without the field declares no reasoning support and behaves exactly as before. A model MAY declare `supportsVision` (boolean, default `false`); `ModelInfo.SupportsVision` SHALL expose it and a model without the field SHALL report no vision support. A user entry SHALL be able to turn the flag on or off for an overridden built-in.

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

#### Scenario: A schema 1 catalog still loads

- **GIVEN** a user `models.json` with `schemaVersion: 1` and a model entry without pricing
- **WHEN** the catalog is loaded
- **THEN** the model is available with no pricing, no maximum output, vision off, and its own id as the wire id

#### Scenario: The managed cache merges below the user file

- **GIVEN** an embedded entry, a managed cache entry with the same id, and a user entry with that id
- **WHEN** the catalog is loaded
- **THEN** the user entry wins, and an id present only in the managed cache is available

#### Scenario: Vision is declared per model

- **GIVEN** a `models.json` entry with `"supportsVision": true` and one without the field
- **WHEN** the catalog is loaded
- **THEN** the first reports vision support and the second reports none

#### Scenario: A user override changes the vision flag

- **GIVEN** a built-in model with `supportsVision` and a user entry redefining it with the opposite value
- **WHEN** the catalog is loaded
- **THEN** the user value is reported

### Requirement: Recording and replay of provider streams
`Lunate.Ai` SHALL record provider exchanges as JSONL when `LUNATE_RECORD=1` for both call styles: a streamed exchange (`GetStreamingResponseAsync`) records its update sequence, and an aggregated exchange (`GetResponseAsync`) records the response as its update sequence so replay serves both call styles from one exchange. `Lunate.Ai` SHALL replay fixtures deterministically: `ReplayChatClient` answers requests from the recorded exchanges in order, verifying each request digest, with actionable errors on mismatch or exhaustion. Fixtures SHALL serialize Microsoft.Extensions.AI types with `AIJsonUtilities` options and replay SHALL NOT require API keys or network access. When `LUNATE_REPLAY` names a fixture path, the factory's standard resolution path SHALL wire `ReplayChatClient` for it without resolving a credential or constructing a provider client, so a session replays with no key and no network; the explicit-credential overload (`Create(model, apiKey)`) SHALL NOT replay, and an explicit recording request takes precedence when both are set. When `LUNATE_RECORD=1` is set without an explicit path, recordings SHALL default under the user's home directory (`~/.lunate/recordings/`), never inside the current working directory. The request digest SHALL cover the messages, the model id and the tool names; tool descriptions and schemas are deliberately outside the digest, so presentation changes do not invalidate a fixture (revisit if it bites). Scripted fixtures SHALL be marked with the model id `synthetic`; live recordings carry the real model id. For the digest, every image `DataContent` in a message SHALL be replaced by its media type and lowercase SHA-256 hex digest, so the image identity is covered deliberately while base64 bytes never enter the digest input or a fixture.

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

#### Scenario: The same image has the same digest

- **GIVEN** two requests whose messages carry identical image bytes in different encodings
- **WHEN** their request digests are computed
- **THEN** the digests match and neither input contains base64 image bytes

#### Scenario: Different pixels change the digest

- **GIVEN** two requests whose image bytes differ by one pixel
- **WHEN** their request digests are computed
- **THEN** the digests differ
