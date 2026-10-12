## MODIFIED Requirements

### Requirement: Project extension trust

Project extensions SHALL require a one-time trust decision per repository identity (host-provided, `GitCommonDir` per the worktree architecture), re-prompted when the extension content hash changes for a worktree. New worktrees of a trusted repository SHALL not re-prompt. Denial SHALL refuse the load. Global extensions SHALL not be gated. Content hashes SHALL be computed over the extension directory's files deterministically, and trust records SHALL be written atomically. In interactive sessions the decision SHALL be asked as a live-area prompt answered with keys through the normal key path (deny by default); print and ACP sessions SHALL refuse an untrusted project extension with a logged hint naming the approval paths — an interactive session or the `lunate trust` command. A re-prompt caused by changed files SHALL say so. After any answer, one notice SHALL state that the decision is remembered for this repository and how to change it: `/trust` lists this repository's records and `/trust clear` clears them, so the next load asks again.

#### Scenario: An untrusted project extension prompts once

- **GIVEN** an untrusted repository with a project extension
- **WHEN** the extension is loaded and approved
- **THEN** the decision is recorded and a later load does not prompt again

#### Scenario: A content change re-prompts

- **GIVEN** a trusted project extension whose files changed
- **WHEN** it is loaded
- **THEN** the user is prompted again

#### Scenario: Denial refuses the load

- **GIVEN** an untrusted project extension
- **WHEN** the user denies the prompt
- **THEN** the load is refused

#### Scenario: Interactive sessions ask in the live area

- **GIVEN** an untrusted repository with a project extension
- **WHEN** an interactive session starts
- **THEN** the question renders in the live area and a `y` key approves it, with no stdin line read

#### Scenario: A re-prompt says the files changed

- **GIVEN** a project extension whose files changed since it was trusted
- **WHEN** an interactive session starts
- **THEN** the question says the files changed since this repository trusted it

#### Scenario: Trust decisions are revisable

- **GIVEN** a repository whose project extension was approved
- **WHEN** `/trust clear` is submitted and a new session starts
- **THEN** the question is asked again

#### Scenario: A scripted run approves through the command

- **GIVEN** an untrusted repository with a project extension
- **WHEN** `lunate trust --yes` runs and a later print session starts
- **THEN** the extension loads and no refusal hint is logged

## ADDED Requirements

### Requirement: The `lunate trust` command

The CLI SHALL provide a `lunate trust` command for scripted and CI runs, operating on the current working directory's repository identity and worktree. With no flags it SHALL print this repository's trust records and every pending decision — a project extension whose hash is unknown or changed, or an untrusted project `mcp.json` — using the same content the interactive trust question shows, and SHALL record nothing. `--yes` SHALL print the same decisions and then record them in the store's sections (extension trust and the project `mcp.json`), including a new worktree of a trusted repository, which a session would record without asking; a second invocation SHALL find nothing pending and succeed. `--clear` SHALL clear the repository's records so the next session asks again. The command SHALL perform discovery and hashing only — it SHALL NOT load extension assemblies, dispatch hooks, or start MCP servers — and the hashes it records SHALL be the hashes it prints. Trust SHALL never be recorded implicitly: no environment variable, configuration key or flag SHALL bypass the explicit invocation. Success exits 0; unknown or conflicting flags print usage on stderr and exit 2.

#### Scenario: Inspect prints the pending decisions and records nothing

- **GIVEN** an untrusted repository with a project extension and a project `mcp.json`
- **WHEN** `lunate trust` runs
- **THEN** it prints the records and both pending decisions with the interactive question's content, and the trust file is unchanged

#### Scenario: Approval records without loading extension code

- **GIVEN** a project extension whose factory would write a marker file
- **WHEN** `lunate trust --yes` runs
- **THEN** the trust records exist and the marker is absent

#### Scenario: Approval is idempotent

- **GIVEN** a repository trusted through the command
- **WHEN** `lunate trust --yes` runs again
- **THEN** nothing new is recorded and the exit code is 0

#### Scenario: Clearing through the command re-asks

- **GIVEN** a repository whose project extension was approved
- **WHEN** `lunate trust --clear` runs and a new session starts
- **THEN** the question is asked again

#### Scenario: Bad usage records nothing

- **GIVEN** conflicting flags such as `--yes --clear`
- **WHEN** the command runs
- **THEN** usage is printed on stderr, the exit code is 2, and nothing is recorded
