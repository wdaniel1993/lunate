## MODIFIED Requirements

### Requirement: Project extension trust

Project extensions SHALL require a one-time trust decision per repository identity (host-provided, `GitCommonDir` per the worktree architecture), re-prompted when the extension content hash changes for a worktree. New worktrees of a trusted repository SHALL not re-prompt. Denial SHALL refuse the load. Global extensions SHALL not be gated. Content hashes SHALL be computed over the extension directory's files deterministically, and trust records SHALL be written atomically. An extension installed by `lunate install` SHALL bind, besides the directory content hash, the package that provided it — the canonical package id, the exact version, the nupkg's SHA-512 and the canonical dependency-lock graph — so an upgraded package re-prompts exactly as changed files do, and an edit inside the installed directory still re-prompts through the content hash. The question for such an extension SHALL name its source package on one line, `from package <id>@<version> (<source>)`. In interactive sessions the decision SHALL be asked as a live-area prompt answered with keys through the normal key path (deny by default); print and ACP sessions SHALL refuse an untrusted project extension with a logged hint naming the approval paths — an interactive session or the `lunate trust` command. A re-prompt caused by changed files SHALL say so. After any answer, one notice SHALL state that the decision is remembered for this repository and how to change it: `/trust` lists this repository's records and `/trust clear` clears them, so the next load asks again.

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

#### Scenario: An installed extension's question names its package

- **GIVEN** an untrusted repository with a project install of `Acme.Toolkit@1.4.2` from `nuget.org`
- **WHEN** a session starts and the extension question renders
- **THEN** the question names the extension id, version and directory and one line reads `from package Acme.Toolkit@1.4.2 (nuget.org)`

#### Scenario: An upgraded package re-prompts

- **GIVEN** a trusted project install of `Acme.Toolkit@1.4.2`
- **WHEN** `lunate install Acme.Toolkit@1.5.0 --project` runs and a session starts
- **THEN** the question is asked again, naming `Acme.Toolkit@1.5.0`

#### Scenario: An edit inside an installed extension re-prompts

- **GIVEN** a trusted project install whose recorded entry assembly is edited by hand
- **WHEN** a session starts
- **THEN** the content hash no longer matches and the question is asked again
