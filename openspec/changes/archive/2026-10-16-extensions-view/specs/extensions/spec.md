## ADDED Requirements

### Requirement: The `/extensions` view

The CLI SHALL provide an interactive `/extensions` command and a `lunate extensions [--json]` subcommand listing every discovered extension — failed first, then trust-denied, then loaded, each group by id — with its id, version, scope (`global`/`project`) and state (`loaded`, `trust-denied` or `failed`). Loaded extensions SHALL show their load time in milliseconds (a load above 500 ms marked `(slow)`) and their **registered** tool, command, hook and service counts, a field differing from the manifest's declared list carrying `(N declared)`; trust-denied and failed extensions SHALL show the manifest's **declared** counts, and a failed extension's message SHALL be truncated to its first sentence followed by ` — see the log` when truncated. The interactive form SHALL render one segment per extension in the single-line notice discipline (segments joined `;`, prefixed `extensions: `); the subcommand SHALL print one line per extension with load times labelled `(CLI-measured)`, or with `--json` a machine-readable array of the same fields (`loadMs`, `slow` where applicable), and SHALL compose the host in register-only inspect mode — loading and registering trusted extensions but starting no session events, background services or MCP servers, unloading each extension and stopping the host before exit — and SHALL never prompt, so an extension it cannot trust reads `trust-denied`. It SHALL exit non-zero when any extension failed. With no extensions discovered, the interactive form SHALL read `extensions: none configured` and `--json` SHALL print `[]`; with extensions disabled (`--no-extensions`) the interactive form SHALL read `extensions disabled (--no-extensions)`. The view SHALL never print settings values, secrets or environment values, and numbers SHALL be formatted culture-invariantly.

#### Scenario: A session shows each extension's scope, state, load time and contributions

- **GIVEN** a session with one loaded global extension and one project extension whose trust was denied
- **WHEN** `/extensions` is submitted
- **THEN** one segment per extension appears ordered failed, trust-denied, loaded — the loaded one showing `(global) loaded in N ms` and its registered counts, the denied one showing `(project) trust-denied` and its declared counts

#### Scenario: The subcommand is register-only and never prompts

- **GIVEN** a store with one trusted global extension whose background service and session hooks write markers, and one untrusted project extension
- **WHEN** `lunate extensions` runs non-interactively
- **THEN** the factory ran (registration marker present) but no service-start or session-hook marker exists, nothing prompted, no trust was recorded, the host stopped, and the exit code is 0; with `--json` the same data prints as an array of objects

#### Scenario: A failed extension makes the subcommand exit non-zero

- **GIVEN** a store with an extension whose load fails
- **WHEN** `lunate extensions` runs
- **THEN** its line reads `failed` with the first sentence of the message and ` — see the log` when truncated, and the exit code is non-zero

#### Scenario: The empty and disabled views

- **GIVEN** no extensions are discovered (or the run carries `--no-extensions`)
- **WHEN** `/extensions` is submitted (or `lunate extensions` runs)
- **THEN** the interactive form reads `extensions: none configured` (`extensions disabled (--no-extensions)` when disabled) and `--json` prints `[]`
