# Delta — extension-contract primitives: `/extensions` view additions for built-ins and activation

## MODIFIED Requirements

### Requirement: The `/extensions` view

The CLI SHALL provide an interactive `/extensions` command and a `lunate extensions [--json]` subcommand listing every discovered extension — failed first, then trust-denied, then loaded, each group by id — with its id, version, scope (`global`/`project`/`builtin`) and state (`loaded`, `trust-denied` or `failed`). Loaded extensions SHALL show their load time in milliseconds (a load above 500 ms marked `(slow)`) and their **registered** tool, command, hook and service counts, a field differing from the manifest's declared list carrying `(N declared)`; trust-denied and failed extensions SHALL show the manifest's **declared** counts, and a failed extension's message SHALL be truncated to its first sentence followed by ` — see the log` when truncated. The interactive form SHALL render one segment per extension in the single-line notice discipline (segments joined `;`, prefixed `extensions: `); the subcommand SHALL print one line per extension with load times labelled `(CLI-measured)`, or with `--json` a machine-readable array of the same fields (`loadMs`, `slow` where applicable), and SHALL compose the host in register-only inspect mode — loading and registering trusted extensions but starting no session events, background services or MCP servers, unloading each extension and stopping the host before exit — and SHALL never prompt, so an extension it cannot trust reads `trust-denied`. It SHALL exit non-zero when any extension failed. A built-in extension SHALL appear with scope `builtin`, state `loaded`, its registered counts, and never `pending` or `trust-denied`; the interactive form SHALL offer an unload/reload action for it and `lunate extensions reload <id>` and unloading by id SHALL be refused with a teaching message naming the built-in and the reason (a built-in is process-lifetime code with no ALC to release). With no extensions discovered, the interactive form SHALL read `extensions: none configured` and `--json` SHALL print `[]`; with extensions disabled (`--no-extensions`) the interactive form SHALL read `extensions disabled (--no-extensions)` and the composed built-in segments (scope `builtin`, state `loaded`) SHALL follow the same reading and listing as with extensions enabled. A loaded extension's enabled tool state SHALL be visible: a declared tool whose `toolActivation` rule is false for the run's workspace SHALL be reported as `inactive (<condition>): <reason>` after the tool counts, and a true rule SHALL count as active, with the registered/declared tool counts unchanged by the rule. The view SHALL never print settings values, secrets or environment values, and numbers SHALL be formatted culture-invariantly.

#### Scenario: A session shows each extension's scope, state, load time and contributions

- **GIVEN** a session with one loaded global extension and one project extension whose trust was denied
- **WHEN** `/extensions` is submitted
- **THEN** one segment per extension appears ordered failed, trust-denied, loaded — the loaded one showing `(global) loaded in N ms` and its registered counts, the denied one showing `(project) trust-denied` and its declared counts

#### Scenario: A built-in segment cannot be unloaded or reloaded

- **GIVEN** a session with one composed built-in extension
- **WHEN** `/extensions` is submitted and unload or reload is attempted for its id
- **THEN** the segment reads `(builtin) loaded` with its registered counts, the attempt is refused with a teaching message naming the built-in, and the built-in stays loaded

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
- **THEN** the interactive form reads `extensions: none configured` (`extensions disabled (--no-extensions)` when disabled) and `--json` prints `[]`, and with a built-in composed under `--no-extensions` the built-in segments follow the same reading and listing as with extensions enabled

#### Scenario: An inactive tool is reported with its rule

- **GIVEN** a loaded extension whose manifest declares a tool with a false activation rule and a tool with a true rule
- **WHEN** `/extensions` is submitted (or `lunate extensions` runs)
- **THEN** the false-rule tool reads `inactive (<condition>): <reason>` and the registered/declared tool counts are unchanged for both tools
