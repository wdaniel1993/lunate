## MODIFIED Requirements

### Requirement: Settings file and precedence

The agent SHALL read user settings from `~/.lunate/settings.json` (`schemaVersion: 1`): optional `model` (default model id), `approval` (`ask` — default — non-read tools require approval; `auto-edit` — file writes and edits are approved automatically, commands still require approval), and `output.toolResultLimit` (default 30,000 characters). The value `yolo` SHALL be rejected: it exists only as a per-run flag, never a saved setting. A missing file SHALL yield defaults; a present file SHALL be validated with all problems collected into one actionable error. Environment variables (`LUNATE_MODEL`, `LUNATE_APPROVAL`, `LUNATE_TOOL_OUTPUT_LIMIT`) SHALL override file values and SHALL be validated identically. The resolved output limit SHALL govern tool-output truncation in the loop.

#### Scenario: Missing file yields defaults

- **GIVEN** no settings file
- **WHEN** settings are resolved
- **THEN** the model is unset, approval is `ask`, and the tool result limit is 30,000

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
