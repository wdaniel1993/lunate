## MODIFIED Requirements

### Requirement: Slash commands

A submitted line whose first non-space character is `/` SHALL dispatch as a client command and never reach the model: `/model` opens the model picker (`/model <id>` switches directly when the id is in the catalog), and when the current model is an extension-registered or virtual model the picker's current-model line and `/model` SHALL show its last routed resolution (`auto → deep`); `/new` starts a fresh session, `/name <title>` names the current session and bare `/name` reports the stored name, `/session` reports the current session's path, id, message count and token totals, `/setup` opens the setup wizard — the entry menu on a configured install, the full walk on a first run (see the `setup-wizard` capability), `/resume` opens the session picker, `/compact` requests an explicit out-of-run compaction, `/thinking <level>` sets the current session's reasoning level only (one of `off`/`low`/`medium`/`high`; `/thinking` alone prints the current level and the active model's mapping; the settings file and the profile stay exactly as they are; a non-`off` level on a model that declares no reasoning style is refused naming the model and the `thinking` setting, and `off` and `inline`-styled models accept), `/mcp` lists the configured and extension-registered MCP servers with their status and source (the config file path it came from, or `extension '{id}'`), with a name collision shown as the configured server winning and the registration marked shadowed, a server whose definition references an unset environment variable without a default shown with that missing-variable warning next to it, and never printing environment values or headers, `/extensions` lists every discovered extension — failed first, then trust-denied, then loaded, each group by id — as one segment per extension: `id version (scope) state`, with loaded ones carrying their load time and registered tool, command, hook and service counts (a field differing from the manifest reads `(N declared)`, a load above 500 ms reads `(slow)`), trust-denied ones reading `trust-denied` with the manifest's declared counts, failed ones reading `failed` followed by the message's first sentence and ` — see the log` — reading `extensions: none configured` when there are none and `extensions disabled (--no-extensions)` when extensions are disabled, `/settings` shows every resolved setting aligned with its settings-file key and the editable keys, refusing keys that have their own command (`/model`, `/favorite`) or are launch-time, `/settings thinking <level>` persists the level into the chat profile's `reasoningLevel` (`off` removes the key) through the store's read-modify-write — unrelated keys survive, a failed write leaves the file intact and keeps the session's own change with a saved-failed notice — refuses when the chat selection is not a configured profile in the user settings file (the selection-derived profile and its location are named, with `/thinking` as the per-session lever) and when the session's model declares no reasoning style, naming the model and the `thinking` setting, `/trust` lists this repository's trust records (`/trust clear` clears them, so the next load asks again), `/editor` opens the draft in the external editor — the same round-trip `Ctrl+G` runs, so the command is the way in when the terminal does not deliver the key or a keybindings file claims it — `/skills` lists the discovered skills with their scope and state, `/skill:<name> [args]` invokes the named skill by submitting its wrapped body as a user turn through the normal submission path — an unknown name warns instead — `/help` lists the commands grouped (session / model / extensions / files / quit) with one-line descriptions taken from the command registry — no hand-maintained list: a command dispatchable without a description SHALL fail a test — `/quit` quits. Commands SHALL append to the input history and skip the input pipeline; an unknown command SHALL produce a dim notice naming it. While a turn is running, `/new`, `/resume`, `/model`, `/compact` and `/setup` SHALL be refused with a notice; `/quit` SHALL quit at any time (cancelling a running turn). While an approval prompt or a picker is open, no input is submitted, so no command can dispatch.

#### Scenario: Every built-in command dispatches without a model call

- **GIVEN** an idle scripted session
- **WHEN** each built-in command is submitted
- **THEN** it takes its action, the request count is unchanged, and the command text is recalled by Up

#### Scenario: Unknown command

- **GIVEN** an idle session
- **WHEN** `/nope` is submitted
- **THEN** a dim notice names the command, no run starts, and nothing reaches the model

#### Scenario: Busy session refusals

- **GIVEN** a running turn
- **WHEN** `/new`, `/resume`, `/model`, `/compact` or `/setup` is submitted
- **THEN** a notice refuses it and the run is untouched

#### Scenario: /compact without a compactable tail

- **GIVEN** an idle session whose history has nothing older than the kept tail
- **WHEN** `/compact` is submitted
- **THEN** a notice says nothing was compacted

#### Scenario: Commands skip the input pipeline

- **GIVEN** an installed consuming input hook
- **WHEN** a command is submitted
- **THEN** the hook never sees the command text and the command still acts

#### Scenario: /mcp lists configured servers with status

- **GIVEN** an idle session with one started and one failing configured MCP server
- **WHEN** `/mcp` is submitted
- **THEN** a notice lists both servers with their status (started with its tool count; failed with the reason), without a model call

#### Scenario: /mcp lists extension-registered servers with status and source

- **GIVEN** an idle session where an extension registered an MCP server and no configured servers exist
- **WHEN** `/mcp` is submitted
- **THEN** a notice lists the extension-registered server with its status and `extension '{id}'` as its source, without a model call

#### Scenario: /mcp shows a name collision with the configured server winning

- **GIVEN** an idle session with a configured server and an extension registration sharing its name
- **WHEN** `/mcp` is submitted
- **THEN** the notice shows the configured server with its status and source, and the registration as shadowed by the configured source, without a model call

#### Scenario: /mcp shows a crashed server as failed

- **GIVEN** a session whose started MCP server process has exited
- **WHEN** `/mcp` is submitted
- **THEN** the notice shows the server as failed with `the server process exited`, and a notice named the server

#### Scenario: /mcp never prints environment values or headers

- **GIVEN** an idle session whose servers carry environment values
- **WHEN** `/mcp` is submitted
- **THEN** the notice contains neither the environment values nor any header names

#### Scenario: /trust lists and clears this repository's records

- **GIVEN** a repository with recorded trust decisions
- **WHEN** `/trust` is submitted
- **THEN** a notice lists them, and after `/trust clear` the records are gone and a later load asks again

#### Scenario: The picker shows routing for a virtual model

- **GIVEN** a session whose model is a virtual model with a recorded last resolution
- **WHEN** the model picker opens
- **THEN** its current-model line shows `auto → deep`

#### Scenario: /mcp shows a missing-variable warning next to the server

- **GIVEN** an idle session whose server definition references an unset `${VAR}` with no default
- **WHEN** `/mcp` is submitted
- **THEN** the server's entry carries the missing-variable warning, and no model call happens

#### Scenario: /extensions lists the discovered extensions with scope, state and contributions

- **GIVEN** an idle session with one loaded global extension and one project extension whose trust was denied
- **WHEN** `/extensions` is submitted
- **THEN** one segment per extension appears ordered failed, trust-denied, loaded — the loaded one showing `(global) loaded in N ms` and its registered counts, the denied one showing `(project) trust-denied` and its declared counts — and no model call happens

#### Scenario: /extensions marks a slow load and truncates a failed message

- **GIVEN** an idle session with a loaded extension whose load exceeded the threshold and a failed extension with a multi-sentence message
- **WHEN** `/extensions` is submitted
- **THEN** the loaded segment carries `(slow)` and the failed segment carries the message's first sentence followed by ` — see the log`

#### Scenario: /extensions with no extensions discovered or disabled

- **GIVEN** an idle session with no extensions discovered (or one started with `--no-extensions`)
- **WHEN** `/extensions` is submitted
- **THEN** the notice reads `extensions: none configured` (or `extensions disabled (--no-extensions)`), and no model call happens

#### Scenario: /skills lists discovered skills with scope and state

- **GIVEN** an idle trusted session with one project skill and one `disable-model-invocation` user skill
- **WHEN** `/skills` is submitted
- **THEN** a notice lists both with their scopes and the command-only marker, and no model call happens

#### Scenario: /skill:name invokes the skill

- **GIVEN** an idle trusted session with skill `greek` discovered
- **WHEN** `/skill:greek` is submitted
- **THEN** the skill body reaches the model as a wrapped user turn and exactly one run starts

#### Scenario: /skill:unknown warns without a run

- **GIVEN** an idle session without a skill named `ghost`
- **WHEN** `/skill:ghost` is submitted
- **THEN** a dim notice reads `unknown skill: ghost (see /skills)` and no run starts

#### Scenario: /help lists every command with a description

- **GIVEN** an idle session
- **WHEN** `/help` is submitted
- **THEN** the listing shows every built-in command grouped (session / model / extensions / files / quit), each with a one-line description, and no command appears without one

#### Scenario: /thinking sets and reports the level

- **GIVEN** an idle session
- **WHEN** `/thinking high` then `/thinking` are submitted
- **THEN** the level is set to `high`, the footer shows `think:high`, and the bare command prints the current level with the active model's mapping — without a model call

#### Scenario: /thinking refuses an unsupported model by name

- **GIVEN** an idle session whose model declares no reasoning style
- **WHEN** `/thinking high` is submitted
- **THEN** a warning names the model id and the `thinking` setting, the session's level stays where it was, the footer never shows the refused level, and no run starts

#### Scenario: /thinking leaves the profile and the settings file as they are

- **GIVEN** an idle session whose chat profile names `reasoningLevel: "high"` in the user settings file
- **WHEN** `/thinking off` is submitted and the session quits
- **THEN** the settings file still carries `reasoningLevel: "high"` and no `thinking` key was written, and a later session on the same file starts at the profile's level again

#### Scenario: /settings thinking persists the chat profile's level

- **GIVEN** an idle session whose chat profile is a configured profile in the user settings file and is a reasoning model
- **WHEN** `/settings thinking low` is submitted
- **THEN** the profile's `reasoningLevel` reads `low` in the file, the running session applies it, and a later session on the same file starts at `low`

#### Scenario: /settings thinking refuses a selection-derived chat profile

- **GIVEN** an idle session whose chat selection is a bare model selection, not a configured profile
- **WHEN** `/settings thinking low` is submitted
- **THEN** a warning names the profile and points at the settings file, nothing is written, and `/thinking` remains the per-session lever

#### Scenario: /editor reaches the editor round-trip

- **GIVEN** an idle session and a scripted editor that writes two lines
- **WHEN** `/editor` is submitted
- **THEN** the editor child ran on the temp file, the prompt holds the editor's text afterwards, and no run starts

#### Scenario: /setup opens the wizard and is refused while busy

- **GIVEN** an idle session, then again a session with a running turn
- **WHEN** `/setup` is submitted
- **THEN** the wizard opens on the idle session (no run starts, no write happens before its summary is confirmed), the busy session is refused with a notice, and the command is recalled by Up

#### Scenario: Quit during a running turn

- **GIVEN** a running turn
- **WHEN** `/quit` is submitted
- **THEN** the turn is cancelled (the run never delivers its remaining output), the session loop ends with the normal quit exit code, and no refusal notice is shown
