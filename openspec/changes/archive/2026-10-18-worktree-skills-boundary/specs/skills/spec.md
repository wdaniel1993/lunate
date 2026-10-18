# skills — delta for worktree-skills-boundary

## MODIFIED Requirements

### Requirement: Skill discovery

The session SHALL discover skills from the worktree root upward to the repository root (never above it); when the repository root is not an ancestor of the worktree root (a linked worktree), discovery is bounded to the worktree root itself and the main checkout is not scanned, checking per directory `.lunate/skills`, then `.agents/skills`, then `.claude/skills`, and afterwards the user scope `~/.lunate/skills`, then `~/.agents/skills`. A skill is a directory containing a `SKILL.md`. Nearest scope wins; within one level the earlier directory wins; shadowing order is project > user > extension (a project skill shadows a same-name user skill, a user skill shadows a same-name extension skill). Every resolved collision SHALL warn once per session naming the winner and the shadowed path, and the winner's entry in `/skills` SHALL carry a `shadows <scope>` marker. Discovery is bounded (a hard cap of 256 scanned skills per scope; unreadable directories warn, never fail).

#### Scenario: A skill at the repository root is found from a subdirectory

- **GIVEN** a trusted repository with `.agents/skills/greek/SKILL.md` and a session started in a nested directory
- **WHEN** the session starts
- **THEN** `greek` is discovered and its index entry lists it

#### Scenario: A project skill shadows a same-name user skill

- **GIVEN** a project skill `greek` and a user skill `greek`
- **WHEN** the session starts
- **THEN** one session warning names the project winner and the shadowed path, and `/skills` lists a single `greek (project)` entry with a `shadows user` marker

#### Scenario: `.lunate` wins over `.agents` at the same level

- **GIVEN** `.lunate/skills/greek/SKILL.md` and `.agents/skills/greek/SKILL.md` in the same directory
- **WHEN** the session starts
- **THEN** the `.lunate` skill is the loaded `greek` and the collision is warned once

#### Scenario: Nothing above the repository root is read

- **GIVEN** a skill directory above the repository root containing a valid skill
- **WHEN** the session starts
- **THEN** that skill is not discovered

#### Scenario: A linked worktree's own skills are discovered

- **GIVEN** a repository whose linked worktree (added with `git worktree add`) contains `.agents/skills/greek/SKILL.md`
- **WHEN** a session starts with the worktree as its workspace root
- **THEN** `greek` is discovered and its index entry lists it

#### Scenario: The main checkout's skills are not scanned from a linked worktree

- **GIVEN** a repository with `.agents/skills/main-only/SKILL.md` and a linked worktree of it without that skill
- **WHEN** a session starts with the worktree as its workspace root
- **THEN** `main-only` is not discovered

#### Scenario: Nothing above a linked worktree's root is read

- **GIVEN** a linked worktree and a skill directory in a directory above the worktree root
- **WHEN** a session starts with the worktree as its workspace root
- **THEN** that skill is not discovered
