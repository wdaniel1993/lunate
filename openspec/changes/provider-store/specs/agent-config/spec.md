# Agent Config Specification Delta

## ADDED Requirements

### Requirement: Provider and profile settings validate at load

User settings SHALL own provider definitions and model profiles. A project settings file MAY reference an existing provider, define a profile using that provider, and lower a limit; it SHALL NOT define a provider, endpoint/base URL, or credential reference. Profile settings SHALL be checked against the selected provider and catalog model at load. A refused project provider field or unsupported profile setting SHALL name the offending key and SHALL fail closed before a secret is resolved or a request is sent.

#### Scenario: Project profile references a user provider

- **GIVEN** project settings that define a profile on an existing user provider and lower a configured limit
- **WHEN** settings load
- **THEN** the profile is accepted and the lower limit applies

#### Scenario: Project provider attack names the offending key

- **GIVEN** project settings that define a provider with an attacker URL and credential reference
- **WHEN** settings load
- **THEN** loading is refused with the offending provider, endpoint, or credential key named, without resolving the credential or contacting the URL

#### Scenario: Unsupported model setting is not dropped

- **GIVEN** a profile with an unsupported setting named `temperature`
- **WHEN** settings load
- **THEN** loading fails with `temperature` and the profile name instead of silently ignoring the setting

## MODIFIED Requirements

### Requirement: Auth command line

`lunate auth` SHALL provide `login`, `list`, `logout` and `status` subcommands that work without the
TUI and without an unbounded network wait. `login <provider>` SHALL read a key from a masked prompt
when stdin is a terminal and from a single stdin line when `--key-stdin` is given; `login` without a
provider SHALL list the provider templates on a terminal and exit 2 with that list on stderr when stdin
is not a terminal, never waiting for input. `list` SHALL enumerate provider entries by entry name,
including multiple entries of one kind, and SHALL report credential present/absent plus bounded
endpoint reachability without sending a credential; it SHALL never print a secret or header value.
`logout <provider>` SHALL remove the named credential entry, SHALL name a still-set environment
variable without its value, and SHALL exit non-zero when nothing was stored. `status` SHALL report the
resolved default provider and whether a credential exists, exiting 0 when it does and 1 when it does
not. No subcommand SHALL place a secret in a log line, an error, a notice or a process argument.

#### Scenario: A piped key is stored without a terminal

- **GIVEN** `auth.json` without a `groq` entry and stdin is a pipe
- **WHEN** `printf '%s\n' "$KEY" | lunate auth login groq --key-stdin` runs
- **THEN** the key is stored under `groq`, the exit code is 0, and the key text appears in no output

#### Scenario: Login without a provider never hangs

- **GIVEN** stdin is not a terminal
- **WHEN** `lunate auth login` runs
- **THEN** the supported provider template ids are written to stderr and the exit code is 2

#### Scenario: Login accepts only known providers

- **GIVEN** an idle terminal and an unknown provider name
- **WHEN** `lunate auth login unknown-provider` runs
- **THEN** it lists the accepted provider names, writes no credential, and makes no network call

#### Scenario: List reports every provider without secrets

- **GIVEN** two `openai` provider entries, one credential present and reachable and one absent and
  unreachable
- **WHEN** `lunate auth list` runs
- **THEN** it shows both entries separately with credential presence and reachability, and prints no
  credential or header value

#### Scenario: List reports the source, never the secret

- **GIVEN** a stored `openai` credential and `GROQ_API_KEY` set for another provider
- **WHEN** `lunate auth list` runs
- **THEN** it reports each provider's credential source/presence and reachability without printing either value

#### Scenario: Status is a script-usable answer

- **GIVEN** no credential anywhere
- **WHEN** `lunate auth status` runs
- **THEN** it reports no usable credential and exits 1

#### Scenario: Logout removes the stored credential

- **GIVEN** a stored `groq` credential and no environment override
- **WHEN** `lunate auth logout groq` runs
- **THEN** the stored entry is removed and the command confirms it without printing the value

#### Scenario: Logout reports an environment override

- **GIVEN** a stored `groq` credential and `GROQ_API_KEY` set
- **WHEN** `lunate auth logout groq` runs
- **THEN** the stored entry is removed and the notice names `GROQ_API_KEY` without its value

#### Scenario: Reachability check does not send credentials

- **GIVEN** a provider with a credential reference and an endpoint that accepts connections
- **WHEN** `lunate auth list` checks reachability
- **THEN** it reports the endpoint reachable without sending the credential
