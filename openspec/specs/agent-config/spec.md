# agent-config Specification

## Purpose
The agent's user-facing configuration: the settings file (default model, approval, output limits), the credential store for named API keys, and the model-discovery helper that drafts catalog entries for OpenAI-compatible endpoints. Environment variables override both files, so automation and local overrides work without editing state; secrets never appear in output or errors.

## Requirements

### Requirement: Settings file and precedence

The agent SHALL read user settings from `~/.lunate/settings.json` (`schemaVersion: 2`; schema 1
SHALL remain readable as the same settings without favourites): optional `model` (default model id),
`approval` (`ask` — default — non-read tools require approval; `auto-edit` — file writes and edits
are approved automatically, commands still require approval), `verify` (`block` — default; `warn`;
`off`), and `output.toolResultLimit` (default 30,000 characters), and `startupHeader` (default
`true`; `false` suppresses the interactive welcome header). It SHALL also read optional `favorites`,
an ordered array of at most eight unique non-empty model ids, and optional `tools.extra`, an array of
optional search tool names (`grep`, `find`, `ls`; default empty), where an unknown name or a name
colliding with a core tool SHALL be rejected with the accepted values. The value `yolo` SHALL be
rejected: it exists only as a per-run flag, never a saved setting. A missing file SHALL yield
defaults; a present file SHALL be validated with all problems collected into one actionable error.
Environment variables (`LUNATE_MODEL`, `LUNATE_APPROVAL`, `LUNATE_TOOL_OUTPUT_LIMIT`,
`LUNATE_STARTUP_HEADER`) SHALL override their corresponding file values and SHALL be validated
identically, with one documented alias: where the file takes a JSON boolean, the environment
variable also accepts `1` and `0` (case-insensitive) beside `true` and `false`, and a rejected value
SHALL name the accepted literals. `verify` is a settings-file value without an environment-variable
override. The resolved output limit SHALL govern tool-output truncation in the loop.

A settings write SHALL be read-modify-write over the existing JSON: unrelated keys and values SHALL
survive, favourites SHALL be validated through the same rules, and the replacement SHALL be atomic.
A failed write SHALL leave the old file intact. A write SHALL emit `schemaVersion: 2`.

#### Scenario: Missing file yields defaults

- **GIVEN** no settings file
- **WHEN** settings are resolved
- **THEN** the model is unset, approval is `ask`, `verify` is `block`, the tool result limit is
  30,000, and favourites are empty

#### Scenario: Environment overrides the file

- **GIVEN** a settings file setting `approval` to `ask`
- **WHEN** `LUNATE_APPROVAL=auto-edit` is set and settings are resolved
- **THEN** the resolved approval is `auto-edit`

#### Scenario: Invalid file reports every problem once

- **GIVEN** a settings file with an unknown key and an out-of-range limit
- **WHEN** settings are resolved
- **THEN** one error names both problems and no partial settings are applied

#### Scenario: Yolo is not a saved setting

- **GIVEN** a settings file with `"approval": "yolo"`
- **WHEN** settings are resolved
- **THEN** one error says yolo is per-run only, and no partial settings are applied

#### Scenario: Missing file enables the startup header

- **GIVEN** no settings file and no environment override
- **WHEN** settings are resolved
- **THEN** the startup header is enabled

#### Scenario: Startup header disabled by file and by environment

- **GIVEN** a settings file setting `startupHeader` to `false`
- **WHEN** settings are resolved with no environment override
- **THEN** the resolved startup header is disabled; and with `LUNATE_STARTUP_HEADER=1` set instead,
  it is enabled again

#### Scenario: Missing file defaults the tool details to collapsed

- **GIVEN** no settings file and no environment override
- **WHEN** settings are resolved
- **THEN** the tool details are `collapsed`

#### Scenario: The file expands the tool details

- **GIVEN** a settings file setting `toolDetails` to `expanded`
- **WHEN** settings are resolved with no environment override
- **THEN** the resolved tool details are `expanded`

#### Scenario: The environment overrides the tool details

- **GIVEN** a settings file setting `toolDetails` to `expanded`
- **WHEN** `LUNATE_TOOL_DETAILS=collapsed` is set and settings are resolved
- **THEN** the resolved tool details are `collapsed`; and with no settings file but
  `LUNATE_TOOL_DETAILS=expanded`, they are `expanded`

#### Scenario: An invalid tool details value is collected with the other problems

- **GIVEN** a settings file setting `toolDetails` to `Expanded` and an unknown key
- **WHEN** settings are resolved
- **THEN** one error names both problems and the accepted `collapsed`/`expanded` literals, and no
  partial settings are applied

#### Scenario: An invalid tool details environment value names its variable

- **GIVEN** `LUNATE_TOOL_DETAILS=full` and no settings file
- **WHEN** settings are resolved
- **THEN** one error names `LUNATE_TOOL_DETAILS` with the accepted `collapsed`/`expanded` literals
  and no partial settings are applied

#### Scenario: A schema 1 settings file still loads

- **GIVEN** a settings file with `schemaVersion: 1` and no favourites
- **WHEN** settings are resolved
- **THEN** every existing setting keeps its meaning, `verify` defaults to `block`, and favourites
  are empty

#### Scenario: Favourites are ordered and bounded

- **GIVEN** a settings file with nine favourites, or a duplicate id
- **WHEN** settings are resolved
- **THEN** one error refuses the list and names the eight-entry/unique rule, and no partial settings
  are applied

#### Scenario: A write preserves unrelated settings

- **GIVEN** a settings file carrying a hand-written `thinking` value
- **WHEN** a favourite is added
- **THEN** the file still has that value, gains the favourite, and carries schema 2

#### Scenario: Verify accepts only its three modes

- **GIVEN** a settings file with `verify` set to a value other than `block`, `warn` or `off`
- **WHEN** settings are resolved
- **THEN** validation reports the `verify` field and accepted values, with no partial settings
  applied

#### Scenario: Warn and off are settings-file values

- **GIVEN** a settings file with `verify` set to `warn` or `off`
- **WHEN** settings are resolved without an environment override
- **THEN** the resolved verification mode is the configured value

### Requirement: Auth store

The agent SHALL read named API keys from `~/.lunate/auth.json` (`schemaVersion: 1`, `keys: { <name>: <secret> }`). Saving SHALL restrict the file to owner-only permissions on POSIX. The provider environment variables (`OPENAI_API_KEY`, `ANTHROPIC_API_KEY`, `OPENROUTER_API_KEY`, `DEEPSEEK_API_KEY`, `MOONSHOT_API_KEY`, `ZHIPU_API_KEY`, `OPENCODE_API_KEY`, `GROQ_API_KEY`, `TOGETHER_API_KEY`, `XAI_API_KEY` and `GOOGLE_API_KEY`, with `GEMINI_API_KEY` as the Google alias) SHALL override the corresponding provider entries; custom named keys have no environment override. Errors SHALL name the missing key but never print a secret value.

`/login <provider>` SHALL accept a known preset id, read the key through a masked input that never enters history, scrollback, the session file or a notice, and save it under the provider name with owner-only permissions. `/logout <provider>` SHALL remove the stored key and save. Both commands SHALL refuse while a turn runs and SHALL make no network call; a logout SHALL state when the provider environment variable is still set, naming the variable but never its value.

#### Scenario: Environment beats the file for provider defaults

- **GIVEN** `keys.openai` in auth.json and `OPENAI_API_KEY` set
- **WHEN** the provider credential is resolved
- **THEN** the environment value is used

#### Scenario: Saved auth files are owner-only

- **GIVEN** an auth store saving to a fresh path
- **WHEN** the file is written on POSIX
- **THEN** its mode is `rw-------`

#### Scenario: Login stores a preset key

- **GIVEN** an idle session and no stored `groq` key
- **WHEN** `/login groq` is submitted and a key is entered
- **THEN** `auth.json` contains the key under `groq` with owner-only permissions, and the key text appears in no frame, history entry or session line

#### Scenario: Login accepts only presets

- **GIVEN** an idle session
- **WHEN** `/login unknown-provider` is submitted
- **THEN** a notice names the supported presets and nothing is written

#### Scenario: Logout removes the stored key

- **GIVEN** a stored `groq` key and no `GROQ_API_KEY`
- **WHEN** `/logout groq` is submitted
- **THEN** the entry is gone and a notice confirms it

#### Scenario: Logout never hides a live environment override

- **GIVEN** a stored `groq` key and `GROQ_API_KEY` set
- **WHEN** `/logout groq` is submitted
- **THEN** the stored entry is removed and the notice says `GROQ_API_KEY` is still set, without printing its value

### Requirement: Model discovery helper

`lunate --discover <name-or-url>` SHALL list the models of an OpenAI-compatible endpoint (catalog name resolves endpoint and key reference; a preset name resolves the preset base URL and key variable; a direct URL is used as given) and SHALL print a `models.json`-shaped draft for the user catalog to standard output, without writing any file. A credential SHALL be sent only to an endpoint the provider owns: a preset's base URL, or a catalog entry's endpoint that equals or is under its preset's base URL. A direct URL and a declared third-party endpoint SHALL send no credential unless the catalog entry carries an explicit `auth` reference (then that named key is sent); a 401/403 from such an endpoint SHALL say that no key was sent and name the `auth` opt-in, never a key value. The listing response is `{ "data": [ { "id": ... } ] }`; unknown metadata (`contextWindow`, `supportsTools`) SHALL be omitted from the draft. A preset target SHALL emit qualified `<provider>/<model>` ids with their `wireId`; a direct URL SHALL keep the bare `openai`-provider draft. HTTP, authentication and payload errors SHALL exit non-zero with a message naming the target and the failure.

#### Scenario: Draft is printed, not written

- **GIVEN** a reachable endpoint listing two models
- **WHEN** `--discover` runs
- **THEN** stdout contains a schema-versioned draft with both ids and no file is created

#### Scenario: Unreachable target

- **GIVEN** an endpoint that answers 503
- **WHEN** `--discover` runs
- **THEN** it exits non-zero and the message names the target and the status

#### Scenario: A preset target resolves its endpoint and key

- **GIVEN** `lunate --discover groq` with `GROQ_API_KEY` set
- **WHEN** discovery runs
- **THEN** it calls the Groq preset base URL with that key and drafts qualified ids

### Requirement: Explicit catalog refresh

`lunate --update-catalog` SHALL fetch models.dev, map it through the catalog mapper, validate every generated entry, and atomically write the managed cache at `~/.lunate/models.catalog.json`. It SHALL print a one-line summary naming the model count, provider count and source hash. Without an output path it SHALL leave `~/.lunate/models.json` untouched. With `--output <path>` (the repo-script form) it SHALL write the embedded-shaped snapshot to that path instead. It SHALL make no model call. A fetch, parse or validation failure SHALL exit non-zero, name the stage and leave every existing file unchanged. The command SHALL dispatch before session composition as a standalone action; combining it with `--acp`, `-p`, `--json` or a session-selection flag SHALL be a usage error, so ACP stdout stays protocol-only.

#### Scenario: Refresh writes the managed cache

- **GIVEN** a reachable models.dev response
- **WHEN** `lunate --update-catalog` runs
- **THEN** the managed cache is replaced atomically and the summary names the counts and hash

#### Scenario: The user file is never rewritten

- **GIVEN** a user `models.json` with a custom entry
- **WHEN** `--update-catalog` runs without an output path
- **THEN** the user file's bytes are unchanged and the custom entry still overrides the snapshot

#### Scenario: The script form writes the embedded snapshot

- **GIVEN** `--update-catalog --output src/Lunate.Ai/models.json`
- **WHEN** the command succeeds
- **THEN** that path holds the deterministic schema-2 snapshot and the managed cache is not touched

#### Scenario: Failure is staged and atomic

- **GIVEN** an unreachable models.dev or an invalid response
- **WHEN** `--update-catalog` runs
- **THEN** it exits non-zero naming the fetch or mapping stage, and no existing catalog file changes

#### Scenario: The command never mixes with ACP

- **GIVEN** `lunate --acp --update-catalog`
- **WHEN** the CLI parses the invocation
- **THEN** it exits with a usage error and writes nothing to stdout

### Requirement: Favourites in settings

The resolved settings SHALL expose an ordered favourites list of at most eight model ids. `/favorite [id]` SHALL append the active model (or the named catalog model) and save; adding a duplicate SHALL be a no-op notice; adding a ninth SHALL be refused. `/unfavorite [id]` SHALL remove the active or named model and save; removing a non-favourite SHALL be a no-op notice. Both commands SHALL refuse while a turn runs, SHALL update the in-memory list, and SHALL make no model call. A write failure SHALL keep the previous list and report an actionable notice. Favourites SHALL be user settings, not session state, so a resumed session reads the current list.

#### Scenario: Favorite adds the active model at the end

- **GIVEN** an idle session on a catalog model
- **WHEN** `/favorite` is submitted
- **THEN** the id is appended to settings, the footer position updates, and no request is sent

#### Scenario: Favorite refuses a ninth entry

- **GIVEN** eight favourites
- **WHEN** another model is favourited
- **THEN** a notice says the limit is eight, the list is unchanged, and no request is sent

#### Scenario: Unfavorite removes the active model

- **GIVEN** the active model is a favourite
- **WHEN** `/unfavorite` is submitted
- **THEN** it is removed from settings and the footer position disappears

#### Scenario: A failed write changes nothing

- **GIVEN** settings whose file cannot be replaced
- **WHEN** `/favorite` is submitted
- **THEN** the previous list stays in memory and on disk, and a notice names the failure

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

### Requirement: Decision layer activation

Decision consumption SHALL be configured by one `decisions` block, with the same shape for every
consumer. The block names the `profile` the layer runs on and each consumer's opt-in: `approvals`
(`off` default), `approvalsThreshold`, `routing` (`off` default), `routingProfiles`, `tool` (`off`
default) and `questions` (`off` default). Without a `profile` the layer is unavailable and nothing
is offered; enabling a consumer without one is not an error. No consumer acts by default, and a
configured decision profile SHALL be required for any decision to be proposed — the layer never
borrows the chat profile. The old flat keys (`decisionMode`, `routingMode`, `routingProfiles`) and
the `usages.decision` assignment are refused like any unknown key. Validation SHALL refuse, with a
message naming the field: unknown keys, unknown consumer literals, an out-of-range
`approvalsThreshold` (a finite number from 0 through 1), unknown profile names behind
`decisions.profile` or `routingProfiles`, fewer than two `routingProfiles` when `routing` is on, and
more than 255. Settings SHALL name only consumers this version defines: model routing, the approval
gate, question suggestions and the `decide` tool. Settings SHALL NOT be able to name an acting
decision consumer whose subject this version does not define.

#### Scenario: Opt-in settings activate shadow consumption

- **GIVEN** a user who opts into shadow-mode consumption for the approval gate
- **WHEN** a session records a decision
- **THEN** the consumer records its would-have result, and execution runs the ordinary approval flow
  regardless of the decision

#### Scenario: No decision profile means off, not error

- **GIVEN** settings that enable a decision consumer but configure no `decisions.profile`
- **WHEN** the session starts and asks for approvals
- **THEN** decisions are reported unavailable, no model call is made for the decision layer, and the
  `decide` tool is not offered

#### Scenario: An unknown acting consumer is refused by settings

- **GIVEN** settings naming an acting decision consumer that no capability defines
- **WHEN** the settings are validated
- **THEN** they are rejected with an error naming the consumer, before any session runs

#### Scenario: The decision block is user-scoped

- **GIVEN** project settings that carry the `decisions` block
- **WHEN** the settings are validated
- **THEN** they are rejected; decision consumers are enabled by the user, never by a repository

### Requirement: The four consumers are separately configured

Each consumer SHALL have its own setting inside the `decisions` block, and enabling one SHALL NOT
enable another. The approval gate (`approvals`) and model routing (`routing`) SHALL each offer a
shadow setting as the user's try-out; question suggestions (`questions`) SHALL be display-only when
enabled; the `decide` tool (`tool`) SHALL be offered only when `profile` resolves and `tool` is on.
A deny recommendation that reaches `approvalsThreshold` SHALL escalate the approval ask; a deny
below it SHALL leave the ordinary approval flow unchanged, and routing SHALL pick the most probable
profile without consulting any threshold.

#### Scenario: Enabling routing does not enable the approval gate

- **GIVEN** settings that enable model routing only
- **WHEN** a tool call needs approval
- **THEN** the decision layer evaluates nothing for that ask, and the ordinary approval flow runs

#### Scenario: The decide tool needs its own switch

- **GIVEN** `decisions.tool` set to `on` with no `decisions.profile`
- **WHEN** the tool list is assembled
- **THEN** the `decide` tool is not offered and the layer reports unavailable without an error state

### Requirement: Provider and profile settings validate at load

User settings SHALL own provider definitions and model profiles. A project settings file MAY reference existing user providers and profiles, define a profile using an existing provider, and lower a limit; it SHALL NOT define a provider, endpoint/base URL, or credential reference. Profile settings SHALL be checked against the selected provider and catalog model at load. A refused project provider field or unsupported profile setting SHALL name the offending key and SHALL fail closed before a secret is resolved or a request is sent.

#### Scenario: Project profile references a user provider

- **GIVEN** project settings that define a profile on an existing user provider and lower a configured limit
- **WHEN** settings load
- **THEN** the profile is accepted and the lower limit applies

#### Scenario: Project usage references an existing user profile

- **GIVEN** project settings that assign a usage to an existing user-defined profile
- **WHEN** settings load
- **THEN** the reference is accepted without redefining the profile's provider

#### Scenario: Project provider attack names the offending key

- **GIVEN** project settings that define a provider with an attacker URL and credential reference
- **WHEN** settings load
- **THEN** loading is refused with the offending provider, endpoint, or credential key named, without resolving the credential or contacting the URL

#### Scenario: Unsupported model setting is not dropped

- **GIVEN** a profile with an unsupported setting named `temperature`
- **WHEN** settings load
- **THEN** loading fails with `temperature` and the profile name instead of silently ignoring the setting
