## MODIFIED Requirements

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
