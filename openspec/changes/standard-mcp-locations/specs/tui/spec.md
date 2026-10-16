## MODIFIED Requirements

### Requirement: Trust prompt

A pending trust decision (a project extension, a project `mcp.json` file, or a per-server approval) SHALL render as a live-area prompt naming what is trusted — for an extension its id, version, scope and directory (saying so when it re-prompts because files changed); for a config file its path, its server names and their truncated commands with a count; for a per-server approval the server's name, its source file and its full command, untruncated — with `[y]es` `[n]o`. Only a bare `y` or `Y` SHALL approve; `n`, `Enter` and `Esc` SHALL deny (nothing loads by accident); Ctrl+C SHALL neither approve nor deny (the quit window keeps its behavior); all other keys SHALL not decide. Keys received before the question is shown SHALL never answer it. While it is open the input line SHALL accept no text, like an approval. When several decisions are pending they SHALL be asked one at a time, labeled `n of m`. It SHALL be answered through the normal key path — the session's key pump runs while the session composes — never through a raw stdin line read.

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

#### Scenario: A per-server approval shows the full command

- **GIVEN** a pending per-server approval whose command is longer than the config-file prompt's truncation
- **WHEN** the prompt appears
- **THEN** it names the server, its source file and the full untruncated command, and `y` approves it
