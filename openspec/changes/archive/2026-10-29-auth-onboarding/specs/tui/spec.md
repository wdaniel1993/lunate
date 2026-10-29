## ADDED Requirements

### Requirement: Provider auth picker

`/auth` SHALL open a picker in the live area listing the presets in their file order followed by a
custom-endpoint row. Arrow keys or `j`/`k` SHALL move, Enter SHALL select, Esc SHALL cancel without a
write. Selecting a preset SHALL open the masked secret prompt the login path already uses; selecting
the custom row SHALL collect id, base URL, model id and key, refusing a non-loopback `http` URL or a
missing model id with the reason. After a successful save the notice SHALL name the provider and point
at `/model`, and SHALL NOT contain the key. `/login <provider>` SHALL remain an alias that skips the
picker and opens the masked prompt directly. The command SHALL refuse while a turn runs, and ordinary
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

### Requirement: First-run auth offer

When the TUI starts with no usable credential and stdin and stdout are terminals, it SHALL open the
auth picker once; Esc SHALL dismiss it and leave the ordinary path, where a run reports the missing
credential. Non-interactive runs, print mode and piped input SHALL NOT prompt and SHALL keep the
explicit error naming the key and the environment variable. The empty-session banner SHALL name `/auth`
as the next step whenever nothing is configured.

#### Scenario: A fresh install offers the picker

- **GIVEN** no credential in the environment or the store and a terminal
- **WHEN** the TUI starts
- **THEN** the auth picker is open, and Esc returns to an empty session

#### Scenario: Print mode never prompts

- **GIVEN** no credential and a print-mode run
- **WHEN** the run starts
- **THEN** it fails with the explicit missing-credential message and no prompt is written

#### Scenario: The banner names the next step

- **GIVEN** an empty session with no credential
- **WHEN** the banner renders
- **THEN** it names `/auth` as the way to configure a provider
