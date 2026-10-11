## MODIFIED Requirements

### Requirement: Project extension trust

Project extensions SHALL require a one-time trust decision per repository identity (host-provided, `GitCommonDir` per the worktree architecture), re-prompted when the extension content hash changes for a worktree. New worktrees of a trusted repository SHALL not re-prompt. Denial SHALL refuse the load. Global extensions SHALL not be gated. Content hashes SHALL be computed over the extension directory's files deterministically, and trust records SHALL be written atomically. In interactive sessions the decision SHALL be asked as a live-area prompt answered with keys through the normal key path (deny by default); print and ACP sessions SHALL refuse an untrusted project extension with a logged hint pointing at the interactive path.

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
