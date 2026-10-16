## ADDED Requirements

### Requirement: The `/extensions` view

The CLI SHALL provide an interactive `/extensions` command and a `lunate extensions [--json]` subcommand listing every discovered extension in discovery order (global scope before project scope): its id, version, scope (`global`/`project`) and state — `loaded`, `trust-denied` or `failed` — with a failed extension's message shown after the state. Loaded extensions SHALL additionally show their load time in milliseconds and their manifest's declared tool, command, hook and service counts. The interactive form SHALL render one segment per extension in the single-line notice discipline (segments joined `;`, prefixed `extensions: `); the subcommand SHALL print one line per extension, or with `--json` a machine-readable array of the same fields, and SHALL compose the host non-interactively — never prompting, so an extension it cannot trust reads `trust-denied` — stopping the host before exit. With no extensions discovered, the interactive form SHALL read `extensions: none configured` and `--json` SHALL print `[]`. The view SHALL never print settings values, secrets or environment values, and numbers SHALL be formatted culture-invariantly.

#### Scenario: A session shows each extension's scope, state, load time and contributions

- **GIVEN** a session with one loaded global extension and one project extension whose trust was denied
- **WHEN** `/extensions` is submitted
- **THEN** one segment per extension appears in discovery order — the loaded one showing `(global) loaded in N ms` and its declared counts, the denied one showing `(project) trust-denied`

#### Scenario: The subcommand degrades to text and JSON without prompting

- **GIVEN** a store with one loadable global extension and one untrusted project extension
- **WHEN** `lunate extensions` runs non-interactively
- **THEN** it prints one line per extension (the global one `loaded` with its load time and counts, the project one `trust-denied`), prompts for nothing, records no trust, and stops the host before exit; with `--json` the same data prints as an array of objects

#### Scenario: The empty view

- **GIVEN** no extensions are discovered
- **WHEN** `/extensions` is submitted (or `lunate extensions` runs)
- **THEN** the interactive form reads `extensions: none configured` and `--json` prints `[]`
