## ADDED Requirements

### Requirement: Trust prompt

A pending trust decision (a project extension or a project `mcp.json`) SHALL render as a live-area prompt naming what is trusted — for an extension its id, version, scope and directory; for a config file its path — with `[y]es` `[n]o`. Only a bare `y` SHALL approve; `n`, `Enter` and `Esc` SHALL deny (nothing loads by accident); all other keys SHALL not decide. While it is open the input line SHALL accept no text, like an approval. It SHALL be answered through the normal key path — the session's key pump runs while the session composes — never through a raw stdin line read.

#### Scenario: y approves

- **GIVEN** a pending trust prompt
- **WHEN** `y` is pressed
- **THEN** the decision is approve

#### Scenario: n, Enter and Esc deny

- **GIVEN** a pending trust prompt
- **WHEN** `n`, Enter or Esc is pressed
- **THEN** the decision is deny

#### Scenario: Other keys do not decide

- **GIVEN** a pending trust prompt
- **WHEN** any other key is pressed
- **THEN** the prompt stays pending and no text reaches the input line

#### Scenario: The prompt is answered while the session composes

- **GIVEN** an untrusted project extension and an interactive session starting
- **WHEN** the trust prompt appears during composition
- **THEN** keys answer it through the same key path the running session uses

## MODIFIED Requirements

### Requirement: Interactive session wiring

The interactive session SHALL consume the harness's event stream: streaming text renders as a live tail whose completed paragraphs commit to scrollback as Markdown; tool results commit as tool blocks; usage updates the footer; retrying, compaction and step-limit appear as notices. The approval prompt SHALL appear in the live area and block new input until decided; `always` memory SHALL be session-scoped per tool name. Input history SHALL live in `~/.lunate/history` and be navigable with Up/Down. The live area and the key pump SHALL run while the session composes, so session-start questions are answered through the normal key path; until composition completes, ordinary input stays inert (only prompt keys and the quit window act).

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
