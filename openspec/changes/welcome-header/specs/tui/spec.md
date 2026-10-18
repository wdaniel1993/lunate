# tui — welcome header delta

## ADDED Requirements

### Requirement: Welcome header

An interactive session SHALL print a welcome header once per session start, on the first frame, as finished Spectre output into scrollback — never as a splash: no animation, no blocking, no alternate screen, and the composition (extensions, MCP, skills) SHALL keep running underneath while the `loading project…` live line continues. The header render SHALL be part of the startup budget gate. Content, top to bottom: the crescent wordmark (variant V2, the small crescent lockup, as chosen in the design sign-off) with the version; the model id with its context window; the workspace path with the home directory shortened to `~`; `C# workspace · Roslyn` when a C# workspace is detected; the extension/MCP/skill counts (an `…` placeholder until composition finishes, then exactly one in-place update line — never a redraw of the header block); and a hint line naming `/help`. When composition finds zero of a kind, the counts stay honest (`extensions 0 · MCP 0 · skills 0`).

The header SHALL be capability-aware: the full block only at width ≥ 80 with Unicode support; otherwise a single-line ASCII form (`lunate v0.1.0 (C# · Roslyn) — /help for commands`). `NO_COLOR` SHALL leave the structure unchanged with colors off; a terminal without ANSI support (legacy console, `TERM=dumb`) SHALL get the ASCII form without colors. The header SHALL never appear in print mode, ACP or `--json`. The `startupHeader` setting (default on) and the `LUNATE_STARTUP_HEADER` environment override SHALL suppress it; `/new` SHALL show it again (a session start re-evaluates nothing else).

When no model is configured, or no provider or key resolves, the header SHALL print exactly one actionable fix — no `model` setting and no `LUNATE_MODEL`: `set 'model' in ~/.lunate/settings.json`; no key file: `add the '<provider>' key to ~/.lunate/auth.json`; an environment variable present but not resolving: `set <VARIABLE> to a valid key` — and SHALL print no counts or hint block in that state. The workspace line stays in every state.

#### Scenario: Header prints once on the first frame

- **GIVEN** an interactive session at width ≥ 80 with Unicode and a resolved provider
- **WHEN** the session starts
- **THEN** the header block (wordmark V2, version, model id and context window, `~`-shortened workspace, hint line with `/help`) is printed once into scrollback before composition output, and the `loading project…` line continues

#### Scenario: Counts fill in with a single update line

- **GIVEN** a session whose header printed with the `…` placeholder
- **WHEN** composition completes
- **THEN** exactly one counts line (`extensions N · MCP N · skills N`) is printed below — the header block is not redrawn

#### Scenario: ASCII fallback on narrow or non-Unicode terminals

- **GIVEN** a terminal at width < 80, or without Unicode support
- **WHEN** the session starts
- **THEN** the single-line ASCII form is printed and no block art appears

#### Scenario: First run without a provider

- **GIVEN** no key file and no environment key that resolves
- **WHEN** the session starts
- **THEN** the header names exactly one fix (`add the 'openai' key to ~/.lunate/auth.json`) and prints no counts or hint block

#### Scenario: First run without a model

- **GIVEN** no `model` setting and no `LUNATE_MODEL`
- **WHEN** the session starts
- **THEN** the header names exactly one fix (`set 'model' in ~/.lunate/settings.json`), keeps the workspace line, and prints no counts or hint block

#### Scenario: Machine modes never show it

- **GIVEN** print mode, ACP or `--json`
- **WHEN** a run starts
- **THEN** no welcome header is rendered

#### Scenario: Setting and environment suppress the header

- **GIVEN** `startupHeader` set to `false` (or `LUNATE_STARTUP_HEADER=0`)
- **WHEN** an interactive session starts, and again after `/new`
- **THEN** no header is printed in either case

## MODIFIED Requirements

### Requirement: Slash commands

A submitted line whose first non-space character is `/` SHALL dispatch as a client command and never reach the model: `/model` opens the model picker (`/model <id>` switches directly when the id is in the catalog), and when the current model is an extension-registered or virtual model the picker's current-model line and `/model` SHALL show its last routed resolution (`auto → deep`); `/new` starts a fresh session, `/resume` opens the session picker, `/compact` requests an explicit out-of-run compaction, `/mcp` lists the configured and extension-registered MCP servers with their status and source (the config file path it came from, or `extension '{id}'`), with a name collision shown as the configured server winning and the registration marked shadowed, a server whose definition references an unset environment variable without a default shown with that missing-variable warning next to it, and never printing environment values or headers, `/extensions` lists every discovered extension — failed first, then trust-denied, then loaded, each group by id — as one segment per extension: `id version (scope) state`, with loaded ones carrying their load time and registered tool, command, hook and service counts (a field differing from the manifest reads `(N declared)`, a load above 500 ms reads `(slow)`), trust-denied ones reading `trust-denied` with the manifest's declared counts, failed ones reading `failed` followed by the message's first sentence and ` — see the log` — reading `extensions: none configured` when there are none and `extensions disabled (--no-extensions)` when extensions are disabled, `/trust` lists this repository's trust records (`/trust clear` clears them, so the next load asks again), `/skills` lists the discovered skills with their scope and state, `/skill:<name> [args]` invokes the named skill by submitting its wrapped body as a user turn through the normal submission path — an unknown name warns instead — `/help` lists the commands grouped (session / model / extensions / files / quit) with one-line descriptions taken from the command registry — no hand-maintained list: a command dispatchable without a description SHALL fail a test — `/quit` quits. Commands SHALL append to the input history and skip the input pipeline; an unknown command SHALL produce a dim notice naming it. While a turn is running, `/new`, `/resume`, `/model` and `/compact` SHALL be refused with a notice; `/quit` SHALL quit at any time (cancelling a running turn). While an approval prompt or a picker is open, no input is submitted, so no command can dispatch.

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
- **WHEN** `/new`, `/resume`, `/model` or `/compact` is submitted
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
