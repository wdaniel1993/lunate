## ADDED Requirements

### Requirement: Trust prompt

A pending trust decision (a project extension or a project `mcp.json`) SHALL render as a live-area prompt naming what is trusted — for an extension its id, version, scope and directory (saying so when it re-prompts because files changed); for a config file its path, its server names and their truncated commands with a count — with `[y]es` `[n]o`. Only a bare `y` or `Y` SHALL approve; `n`, `Enter` and `Esc` SHALL deny (nothing loads by accident); Ctrl+C SHALL neither approve nor deny (the quit window keeps its behavior); all other keys SHALL not decide. Keys received before the question is shown SHALL never answer it. While it is open the input line SHALL accept no text, like an approval. When several decisions are pending they SHALL be asked one at a time, labeled `n of m`. It SHALL be answered through the normal key path — the session's key pump runs while the session composes — never through a raw stdin line read.

#### Scenario: y or Y approves

- **GIVEN** a pending trust prompt
- **WHEN** `y` or `Y` is pressed
- **THEN** the decision is approve

#### Scenario: n, Enter and Esc deny

- **GIVEN** a pending trust prompt
- **WHEN** `n`, Enter or Esc is pressed
- **THEN** the decision is deny

#### Scenario: Ctrl+C does not decide

- **GIVEN** a pending trust prompt
- **WHEN** Ctrl+C is pressed
- **THEN** the quit window acts and the prompt stays undecided

#### Scenario: Type-ahead never answers

- **GIVEN** keys typed while the session is still loading, before the question is shown
- **WHEN** the trust question appears
- **THEN** those keys never answer it and the prompt stays pending

#### Scenario: Other keys do not decide

- **GIVEN** a pending trust prompt
- **WHEN** any other key is pressed
- **THEN** the prompt stays pending and no text reaches the input line

#### Scenario: Several decisions are asked one at a time

- **GIVEN** two pending trust decisions at session start
- **WHEN** the questions are asked
- **THEN** the first shows `1 of 2` and is answered before the second shows `2 of 2`

#### Scenario: The prompt is answered while the session composes

- **GIVEN** an untrusted project extension and an interactive session starting
- **WHEN** the trust prompt appears during composition
- **THEN** keys answer it through the same key path the running session uses

## MODIFIED Requirements

### Requirement: Interactive session wiring

The interactive session SHALL consume the harness's event stream: streaming text renders as a live tail whose completed paragraphs commit to scrollback as Markdown; tool results commit as tool blocks; usage updates the footer; retrying, compaction and step-limit appear as notices. The approval prompt SHALL appear in the live area and block new input until decided; `always` memory SHALL be session-scoped per tool name. Input history SHALL live in `~/.lunate/history` and be navigable with Up/Down. The live area and the key pump SHALL run while the session composes, so session-start questions are answered through the normal key path; until composition completes, ordinary input stays inert (only prompt keys and the quit window act) and the footer SHALL show a `loading project…` status (ASCII fallback `loading project...`).

#### Scenario: Streaming commits completed paragraphs

- **GIVEN** a run whose text output spans several paragraphs
- **WHEN** paragraphs complete during streaming
- **THEN** completed paragraphs are rendered as Markdown in scrollback and only the unfinished tail stays live

#### Scenario: Approval blocks steering

- **GIVEN** an open approval prompt
- **WHEN** the user types
- **THEN** the input line accepts no new text; only prompt keys and the quit window act

#### Scenario: Input stays inert during composition

- **GIVEN** a session still composing, for example with a trust prompt open
- **WHEN** the user types ordinary text
- **THEN** the input line accepts no text and submitting is inert; only prompt keys and the quit window act

#### Scenario: Loading status during composition

- **GIVEN** a session that is still composing
- **WHEN** the footer renders
- **THEN** it shows the loading status until the real status replaces it

### Requirement: Slash commands

A submitted line whose first non-space character is `/` SHALL dispatch as a client command and never reach the model: `/model` opens the model picker (`/model <id>` switches directly when the id is in the catalog), `/new` starts a fresh session, `/resume` opens the session picker, `/compact` requests an explicit out-of-run compaction, `/mcp` lists the configured MCP servers with their status, `/trust` lists this repository's trust records (`/trust clear` clears them, so the next load asks again), `/quit` quits. Commands SHALL append to the input history and skip the input pipeline; an unknown command SHALL produce a dim notice naming it. While a turn is running, `/new`, `/resume`, `/model` and `/compact` SHALL be refused with a notice; `/quit` SHALL quit at any time (cancelling a running turn). While an approval prompt or a picker is open, no input is submitted, so no command can dispatch.

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

#### Scenario: /trust lists and clears this repository's records

- **GIVEN** a repository with recorded trust decisions
- **WHEN** `/trust` is submitted
- **THEN** a notice lists them, and after `/trust clear` the records are gone and a later load asks again
