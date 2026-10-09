# agent-config Specification

## Purpose
The agent's user-facing configuration: the settings file (default model, approval, output limits), the credential store for named API keys, and the model-discovery helper that drafts catalog entries for OpenAI-compatible endpoints. Environment variables override both files, so automation and local overrides work without editing state; secrets never appear in output or errors.

## Requirements

### Requirement: Settings file and precedence

The agent SHALL read user settings from `~/.lunate/settings.json` (`schemaVersion: 1`): optional `model` (default model id), `approval` (`ask` — default — requires approval for Execute-risk tool calls once the approval flow exists; `auto` — approved automatically), and `output.toolResultLimit` (default 30,000 characters). A missing file SHALL yield defaults; a present file SHALL be validated with all problems collected into one actionable error. Environment variables (`LUNATE_MODEL`, `LUNATE_APPROVAL`, `LUNATE_TOOL_OUTPUT_LIMIT`) SHALL override file values. The resolved output limit SHALL govern tool-output truncation in the loop.

#### Scenario: Missing file yields defaults

- **GIVEN** no settings file
- **WHEN** settings are resolved
- **THEN** the model is unset, approval is `ask`, and the tool result limit is 30,000

#### Scenario: Environment overrides the file

- **GIVEN** a settings file setting `approval` to `ask`
- **WHEN** `LUNATE_APPROVAL=auto` is set and settings are resolved
- **THEN** the resolved approval is `auto`

#### Scenario: Invalid file reports every problem once

- **GIVEN** a settings file with an unknown key and an out-of-range limit
- **WHEN** settings are resolved
- **THEN** one error names both problems and no partial settings are applied

### Requirement: Auth store

The agent SHALL read named API keys from `~/.lunate/auth.json` (`schemaVersion: 1`, `keys: { <name>: <secret> }`). Saving SHALL restrict the file to owner-only permissions on POSIX. The provider environment variables (`OPENAI_API_KEY`, `ANTHROPIC_API_KEY`) SHALL override the corresponding provider entries; custom named keys have no environment override. Errors SHALL name the missing key but never print a secret value.

#### Scenario: Environment beats the file for provider defaults

- **GIVEN** `keys.openai` in auth.json and `OPENAI_API_KEY` set
- **WHEN** the provider credential is resolved
- **THEN** the environment value is used

#### Scenario: Saved auth files are owner-only

- **GIVEN** an auth store saving to a fresh path
- **WHEN** the file is written on POSIX
- **THEN** its mode is `rw-------`

### Requirement: Model discovery helper

`lunate --discover <name-or-url>` SHALL list the models of an OpenAI-compatible endpoint (catalog name resolves endpoint and key reference; a direct URL is used as given) and SHALL print a `models.json`-shaped draft for the user catalog to standard output, without writing any file. The listing response is `{ "data": [ { "id": ... } ] }`; unknown metadata (`contextWindow`, `supportsTools`) SHALL be omitted from the draft. HTTP, authentication and payload errors SHALL exit non-zero with a message naming the target and the failure.

#### Scenario: Draft is printed, not written

- **GIVEN** a reachable endpoint listing two models
- **WHEN** `--discover` runs
- **THEN** stdout contains a schema-versioned draft with both ids and no file is created

#### Scenario: Unreachable target

- **GIVEN** an endpoint that answers 503
- **WHEN** `--discover` runs
- **THEN** it exits non-zero and the message names the target and the status
