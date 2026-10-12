## MODIFIED Requirements

### Requirement: Slash commands

A submitted line whose first non-space character is `/` SHALL dispatch as a client command and never reach the model: `/model` opens the model picker (`/model <id>` switches directly when the id is in the catalog), `/new` starts a fresh session, `/resume` opens the session picker, `/compact` requests an explicit out-of-run compaction, `/mcp` lists the configured and extension-registered MCP servers with their status and source (the config file path it came from, or `extension '{id}'`), with a name collision shown as the configured server winning and the registration marked shadowed, and never printing environment values or headers, `/trust` lists this repository's trust records (`/trust clear` clears them, so the next load asks again), `/quit` quits. Commands SHALL append to the input history and skip the input pipeline; an unknown command SHALL produce a dim notice naming it. While a turn is running, `/new`, `/resume`, `/model` and `/compact` SHALL be refused with a notice; `/quit` SHALL quit at any time (cancelling a running turn). While an approval prompt or a picker is open, no input is submitted, so no command can dispatch.

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
