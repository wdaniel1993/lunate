## MODIFIED Requirements

### Requirement: Provider auth picker

`/auth` SHALL open a picker in the live area listing the presets in their file order followed by a
custom-endpoint row. Arrow keys or `j`/`k` SHALL move, Enter SHALL select, Esc SHALL cancel without a
write. Selecting a preset SHALL open the masked secret prompt the login path already uses; selecting
the custom row SHALL collect id, base URL, model id and key, reading the key through the masked
prompt and the other three values through the visible prompt, which echoes what is typed, including
spaces — refusing a non-loopback `http` URL or a missing model id with the reason. After a successful
save the notice SHALL name the provider and point at `/model`, and SHALL NOT contain the key.
`/login <provider>` SHALL remain an alias that skips the picker and opens the masked prompt directly.
While the masked prompt is open its key text SHALL NOT appear in the live state or any frame; the
visible prompts carry their typed text. The command SHALL refuse while a turn runs, and ordinary
input SHALL be inert while the picker is open. An optional verification SHALL ask first, perform one
minimal request, and on failure report the failure while keeping the stored key.

#### Scenario: The picker lists every way in

- **GIVEN** an idle session with no credential
- **WHEN** `/auth` is submitted
- **THEN** the live area lists the twelve presets and the custom row, with the first entry selected

#### Scenario: Selecting a preset saves as login does

- **GIVEN** the picker open on `groq`
- **WHEN** Enter is pressed, a key is typed and Enter is pressed again
- **THEN** the key is stored under `groq`, the frames show only the mask, and the notice names the
  provider and `/model`

#### Scenario: A failed verification keeps the key

- **GIVEN** a key just saved for a provider whose endpoint is unreachable
- **WHEN** the optional verification is accepted and fails
- **THEN** the notice reports the failure and the stored key is unchanged

#### Scenario: Esc cancels without writing

- **GIVEN** the picker open
- **WHEN** Esc is pressed
- **THEN** no prompt opens, no write happens, and the session is unchanged

#### Scenario: The alias still works

- **GIVEN** an idle session
- **WHEN** `/login groq` is submitted
- **THEN** the masked prompt opens without the picker


#### Scenario: The custom row echoes every non-key field

- **GIVEN** the custom row selected in the auth picker
- **WHEN** the provider id, base URL and model id are typed, and then an API key
- **THEN** the three visible prompts render and record what was typed, including spaces, and only the key prompt renders one asterisk per character, with its plaintext never in the live state
