## MODIFIED Requirements

### Requirement: Workspace boundary
A `Workspace` SHALL resolve every file-tool path to a canonical absolute path: symlinks resolved for every existing component, the final target deciding whether the path is inside, relative paths combined with the working directory. A path SHALL be accepted only when the canonical target equals an allowed root or sits under it; allowed roots are the working directory plus explicitly configured extra roots. The comparison SHALL be case-insensitive on Windows and macOS and case-sensitive on Linux. Refusals SHALL name the path, the resolved target and the allowed roots. The workspace SHALL expose its identity: `WorktreeRoot` (the canonical run checkout), and — detected from the file system without running git — `RepoRoot` (the main worktree's root) and `GitCommonDir` (the shared git directory), both null outside a repository. Boundary checks SHALL use `WorktreeRoot`; worktrees of one repository SHALL be separate boundaries unless explicitly granted as extra roots.

#### Scenario: A path inside resolves with its display form
- **GIVEN** a workspace rooted at a directory and a file inside it
- **WHEN** a relative or absolute path to that file is resolved
- **THEN** it is accepted, and the display form is relative to the root with forward slashes

#### Scenario: Lexical escape is refused
- **GIVEN** a path that climbs out of the workspace with `..`
- **WHEN** it is resolved
- **THEN** it is refused with an error naming the resolved target and the allowed roots

#### Scenario: A symlink pointing outside is refused
- **GIVEN** a symlink inside the workspace whose target is outside every allowed root
- **WHEN** a path through the symlink is resolved
- **THEN** it is refused

#### Scenario: A symlink pointing inside is allowed
- **GIVEN** a symlink inside the workspace whose target is inside the workspace
- **WHEN** a path through the symlink is resolved
- **THEN** it is accepted, and the canonical target is reported

#### Scenario: A dangling symlink pointing outside is refused
- **GIVEN** a symlink whose target does not exist and lies outside every allowed root
- **WHEN** a path through the symlink is resolved
- **THEN** it is refused — a write through it must not create the target outside the boundary

#### Scenario: A symlink cycle is a resolution error
- **GIVEN** symlinks forming a cycle
- **WHEN** a path through them is resolved
- **THEN** the resolution fails with an error instead of hanging or overflowing

#### Scenario: Case variants follow the file system
- **GIVEN** a workspace on a case-insensitive file system and a path differing only in case from a real path
- **WHEN** it is resolved
- **THEN** it is accepted; on a case-sensitive file system the same path is refused when it does not exist

#### Scenario: An extra root widens the boundary
- **GIVEN** a workspace with an extra allowed root
- **WHEN** a path under that root is resolved
- **THEN** it is accepted

#### Scenario: Repository identity is detected from the file system
- **GIVEN** a linked worktree (a `.git` file with `gitdir:` and a `commondir` file)
- **WHEN** a workspace is created for it
- **THEN** `WorktreeRoot`, `RepoRoot` and `GitCommonDir` are reported — without running git

#### Scenario: A non-repository workspace has no identity
- **GIVEN** a directory without `.git`
- **WHEN** a workspace is created for it
- **THEN** the identity members are null and the boundary still works

#### Scenario: Worktrees of one repository are separate boundaries
- **GIVEN** two worktrees of one repository
- **WHEN** a path in the other worktree is resolved from one of them
- **THEN** it is refused unless the other worktree is explicitly granted as an extra root
