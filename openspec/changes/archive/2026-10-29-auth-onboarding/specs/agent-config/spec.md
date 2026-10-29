## ADDED Requirements

### Requirement: Auth command line

`lunate auth` SHALL provide `login`, `list`, `logout` and `status` subcommands that work without the
TUI and without a network call. `login <provider>` SHALL read a key from a masked prompt when stdin is
a terminal and from a single stdin line when `--key-stdin` is given; `login` without a provider SHALL
list the presets on a terminal and exit 2 with that list on stderr when stdin is not a terminal, never
waiting for input. `list` SHALL show, per provider, whether the credential comes from the environment
or the auth store and SHALL never print a value. `logout <provider>` SHALL remove the entry, SHALL name
a still-set environment variable without its value, and SHALL exit non-zero when nothing was stored.
`status` SHALL report the resolved default provider and whether a credential exists, exiting 0 when it
does and 1 when it does not. No subcommand SHALL place a secret in a log line, an error, a notice or a
process argument.

#### Scenario: A piped key is stored without a terminal

- **GIVEN** `auth.json` without a `groq` entry and stdin is a pipe
- **WHEN** `printf '%s\n' "$KEY" | lunate auth login groq --key-stdin` runs
- **THEN** the key is stored under `groq`, the exit code is 0, and the key text appears in no output

#### Scenario: Login without a provider never hangs

- **GIVEN** stdin is not a terminal
- **WHEN** `lunate auth login` runs
- **THEN** the supported preset ids are written to stderr and the exit code is 2

#### Scenario: List reports the source, never the secret

- **GIVEN** a stored `openai` key and `GROQ_API_KEY` set in the environment
- **WHEN** `lunate auth list` runs
- **THEN** `openai` is reported as stored, `groq` as from the environment, and neither value is printed

#### Scenario: Status is a script-usable answer

- **GIVEN** no credential anywhere
- **WHEN** `lunate auth status` runs
- **THEN** it reports no usable credential and exits 1

### Requirement: Custom OpenAI-compatible provider

The auth surface SHALL accept a custom OpenAI-compatible endpoint — an id, a base URL, a model id and
a key — and SHALL store it as a settings provider entry whose credential is an auth-store key name, so
the key lives in exactly one place. Saving SHALL require an `https` URL, or `http` only for a loopback
host, and SHALL refuse a missing model id with the reason. The refused input SHALL NOT be echoed. A
stored custom provider SHALL resolve through the same client factory path as a preset.

#### Scenario: A local server is accepted over loopback HTTP

- **GIVEN** the picker's custom row and a base URL of `http://localhost:11434/v1`
- **WHEN** the entry is saved with a model id and a key
- **THEN** the provider is stored, and building its client succeeds without a network call

#### Scenario: A non-loopback plain-HTTP URL is refused

- **GIVEN** a base URL of `http://example.com/v1`
- **WHEN** the entry is saved
- **THEN** it is refused with the scheme as the reason and nothing is written

#### Scenario: A missing model id is refused

- **GIVEN** a custom entry with a base URL and no model id
- **WHEN** it is saved
- **THEN** it is refused, the reason names the model id, and the key text is not echoed
