# tui Specification

## MODIFIED Requirements

### Requirement: Status footer

The footer SHALL show model, tokens used against the context window with percent, working directory and git branch as one dim line, formatted culture-invariantly. When the thinking level is not `off`, a `think:<level>` segment SHALL appear after the usage segment. When the session's model is a virtual or extension-registered model, the model segment SHALL show the last routed resolution (`auto → deep`), updating as `ModelRouted` events arrive. Extension status widgets, when registered, SHALL append their segments after the core fields in registration order. It SHALL degrade deterministically at narrow widths (branch dropped first, then directory; model and usage survive; widget segments drop before the branch) and omit percent when the window is unknown. The interactive session SHALL provide a `/context` command showing the context usage plus the resolved subagent limits (concurrency, per-child and per-tree token budgets, the fixed depth) with the current tree usage.

#### Scenario: Narrow width degrades in order

- **GIVEN** a footer too wide for the terminal
- **WHEN** it renders
- **THEN** the branch is dropped first, then the directory, and model and usage remain

#### Scenario: No repository, no branch

- **GIVEN** a working directory outside a git repository
- **WHEN** the footer renders
- **THEN** the branch segment is absent and the rest renders normally

#### Scenario: Widget segments append after the core fields

- **GIVEN** a registered status widget returning a segment
- **WHEN** the footer renders
- **THEN** the segment appears after model, usage, directory and branch, and drops before them at narrow widths

#### Scenario: /context lists the subagent limits

- **GIVEN** an interactive session after a subagent ran
- **WHEN** `/context` is entered
- **THEN** the context usage and the resolved subagent limits with the current tree usage are shown

#### Scenario: The footer shows the last route

- **GIVEN** a session on a virtual model
- **WHEN** a call routes to the `deep` backend
- **THEN** the footer's model segment shows `auto → deep`

#### Scenario: The footer shows the thinking level

- **GIVEN** a session with thinking level `medium`
- **WHEN** the footer renders
- **THEN** the segment `think:medium` appears after the usage segment, and at level `off` it is absent

### Requirement: Slash commands

A submitted line whose first non-space character is `/` SHALL dispatch as a client command and never reach the model: `/model` opens the model picker (`/model <id>` switches directly when the id is in the catalog), and when the current model is an extension-registered or virtual model the picker's current-model line and `/model` SHALL show its last routed resolution (`auto → deep`); `/new` starts a fresh session, `/resume` opens the session picker, `/compact` requests an explicit out-of-run compaction, `/thinking <level>` sets the session's reasoning level (one of `off`/`low`/`medium`/`high`; `/thinking` alone prints the current level and the active model's mapping), `/mcp` lists the configured and extension-registered MCP servers with their status and source (the config file path it came from, or `extension '{id}'`), with a name collision shown as the configured server winning and the registration marked shadowed, a server whose definition references an unset environment variable without a default shown with that missing-variable warning next to it, and never printing environment values or headers, `/extensions` lists every discovered extension — failed first, then trust-denied, then loaded, each group by id — as one segment per extension: `id version (scope) state`, with loaded ones carrying their load time and registered tool, command, hook and service counts (a field differing from the manifest reads `(N declared)`, a load above 500 ms reads `(slow)`), trust-denied ones reading `trust-denied` with the manifest's declared counts, failed ones reading `failed` followed by the message's first sentence and ` — see the log` — reading `extensions: none configured` when there are none and `extensions disabled (--no-extensions)` when extensions are disabled, `/trust` lists this repository's trust records (`/trust clear` clears them, so the next load asks again), `/skills` lists the discovered skills with their scope and state, `/skill:<name> [args]` invokes the named skill by submitting its wrapped body as a user turn through the normal submission path — an unknown name warns instead — `/help` lists the commands grouped (session / model / extensions / files / quit) with one-line descriptions taken from the command registry — no hand-maintained list: a command dispatchable without a description SHALL fail a test — `/quit` quits. Commands SHALL append to the input history and skip the input pipeline; an unknown command SHALL produce a dim notice naming it. While a turn is running, `/new`, `/resume`, `/model` and `/compact` SHALL be refused with a notice; `/quit` SHALL quit at any time (cancelling a running turn). While an approval prompt or a picker is open, no input is submitted, so no command can dispatch.

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

#### Scenario: /thinking sets and reports the level

- **GIVEN** an idle session
- **WHEN** `/thinking high` then `/thinking` are submitted
- **THEN** the level is set to `high`, the footer shows `think:high`, and the bare command prints the current level with the active model's mapping — without a model call

## ADDED Requirements

### Requirement: Thinking display

Reasoning events SHALL render in the live area in the theme's `thinking` color while they stream — prefixed `⠹ thinking… <elapsed>s`, capped at 12 visible lines (older lines scroll away) — and SHALL commit to the scrollback when the reasoning ends: as one collapsed line `✦ thought for <n>s` (whole seconds from the first to the last reasoning event) by default, or as the full wrapped text (indented two spaces) when the `thinkingFull` setting is enabled. Committed thinking SHALL be driven by the event stream alone, so recorded fixtures render identically. Color SHALL never carry meaning alone: the `✦` glyph and the words `thought for` carry it in NO_COLOR and 16-color modes.

#### Scenario: Live then collapsed

- **GIVEN** a run whose model streams reasoning and then text
- **WHEN** the reasoning ends
- **THEN** the live thinking block disappears and a single `✦ thought for <n>s` line commits to the scrollback

#### Scenario: Full text setting

- **GIVEN** the `thinkingFull` setting enabled
- **WHEN** the reasoning ends
- **THEN** the full reasoning text commits wrapped in the thinking color

#### Scenario: Fixtures replay identically

- **GIVEN** a recorded fixture carrying reasoning events
- **WHEN** it is replayed
- **THEN** the thinking live and commit frames are identical to the recording
