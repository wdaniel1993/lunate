## MODIFIED Requirements

### Requirement: Settings file and precedence

The agent SHALL read user settings from `~/.lunate/settings.json` (`schemaVersion: 2`; schema 1 SHALL remain readable as the same settings without favourites): optional `model` (default model id), `approval` (`ask` — default — non-read tools require approval; `auto-edit` — file writes and edits are approved automatically, commands still require approval), and `output.toolResultLimit` (default 30,000 characters), and `startupHeader` (default `true`; `false` suppresses the interactive welcome header). It SHALL also read optional `favorites`, an ordered array of at most eight unique non-empty model ids. The value `yolo` SHALL be rejected: it exists only as a per-run flag, never a saved setting. A missing file SHALL yield defaults; a present file SHALL be validated with all problems collected into one actionable error. Environment variables (`LUNATE_MODEL`, `LUNATE_APPROVAL`, `LUNATE_TOOL_OUTPUT_LIMIT`, `LUNATE_STARTUP_HEADER`) SHALL override file values and SHALL be validated identically, with one documented alias: where the file takes a JSON boolean, the environment variable also accepts `1` and `0` (case-insensitive) beside `true` and `false`, and a rejected value SHALL name the accepted literals. The resolved output limit SHALL govern tool-output truncation in the loop.

A settings write SHALL be read-modify-write over the existing JSON: unrelated keys and values SHALL survive, favourites SHALL be validated through the same rules, and the replacement SHALL be atomic. A failed write SHALL leave the old file intact. A write SHALL emit `schemaVersion: 2`.

#### Scenario: Missing file yields defaults

- **GIVEN** no settings file
- **WHEN** settings are resolved
- **THEN** the model is unset, approval is `ask`, the tool result limit is 30,000, and favourites are empty

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
- **THEN** the resolved startup header is disabled; and with `LUNATE_STARTUP_HEADER=1` set instead, it is enabled again

#### Scenario: A schema 1 settings file still loads

- **GIVEN** a settings file with `schemaVersion: 1` and no favourites
- **WHEN** settings are resolved
- **THEN** every existing setting keeps its meaning and favourites are empty

#### Scenario: Favourites are ordered and bounded

- **GIVEN** a settings file with nine favourites, or a duplicate id
- **WHEN** settings are resolved
- **THEN** one error refuses the list and names the eight-entry/unique rule, and no partial settings are applied

#### Scenario: A write preserves unrelated settings

- **GIVEN** a settings file carrying a hand-written `thinking` value
- **WHEN** a favourite is added
- **THEN** the file still has that value, gains the favourite, and carries schema 2

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

## ADDED Requirements

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
