# skills — delta for agent-skills

## Purpose

Portable agent skills: project and user `SKILL.md` files discovered, indexed into the system prompt under their own budget, listed and invoked through `/skills` and `/skill:<name>`, gated by repository trust, and extensible by extension-provided skill sources.

## ADDED Requirements

### Requirement: Skill discovery

The session SHALL discover skills from the worktree root upward to the repository root (never above it), checking per directory `.lunate/skills`, then `.agents/skills`, then `.claude/skills`, and afterwards the user scope `~/.lunate/skills`, then `~/.agents/skills`. A skill is a directory containing a `SKILL.md`. Nearest scope wins; within one level the earlier directory wins; shadowing order is project > user > extension (a project skill shadows a same-name user skill, a user skill shadows a same-name extension skill). Every resolved collision SHALL warn once per session naming the winner and the shadowed path, and the winner's entry in `/skills` SHALL carry a `shadows <scope>` marker. Discovery is bounded (a hard cap of 256 scanned skills per scope; unreadable directories warn, never fail).

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

### Requirement: Skill format and validation

A skill SHALL be a directory whose `SKILL.md` carries YAML frontmatter with a non-empty `name` and `description`; `name` SHALL equal the directory name (deterministic `/skill:` addressing) and SHALL match `[a-z0-9-]+`. The optional `allowed-tools` field SHALL be accepted and ignored — it is never a permission grant in any scope. The optional `disable-model-invocation: true` SHALL exclude the skill from the model index while keeping it invokable via `/skill:<name>`. A malformed or unreadable `SKILL.md` SHALL warn once per session and SHALL NOT load.

#### Scenario: A malformed skill warns and does not load

- **GIVEN** a project skill whose `SKILL.md` has no `description`
- **WHEN** the session starts
- **THEN** a warning names the directory and reason, the skill is absent from the index, and `/skills` lists it as `(invalid)` with the reason's first sentence

#### Scenario: `allowed-tools` never grants permissions

- **GIVEN** a skill whose frontmatter lists `allowed-tools: [bash]`
- **WHEN** the skill is loaded and invoked
- **THEN** no permission is granted and every tool call keeps the session's normal approval rules

#### Scenario: A command-only skill stays invokable

- **GIVEN** a trusted project skill with `disable-model-invocation: true`
- **WHEN** the session starts and the user submits `/skill:<name>`
- **THEN** the skill is absent from the model index and the invocation still runs

### Requirement: Model exposure and budget

Session start SHALL append the skills index to the system prompt as a section named `skills`, recorded as a prompt-section entry for deterministic replay, and SHALL build it exactly once per session: never rebuilt mid-run (the prompt prefix stays cache-stable), rebuilt only on a wholesale session reload (`/new`, resume-with-reload, extension reload). Each line SHALL read `- <name>: <description> [<path>]` — the path relative to the worktree when the skill is inside it, otherwise the canonical absolute path (directly usable by the read tool) — with descriptions truncated at 200 characters plus `…` on a code-unit boundary that never splits a surrogate pair; a lead line SHALL state that full bodies load through the read tool. Index order SHALL be project, then user, then extension entries, each group by name (ordinal); overflow SHALL cut strictly from the end and append one footer line `+N more skills (not indexed): <names>` — names only, comma-separated, truncated at 400 characters with a trailing `…` — pointing the model at names it can act on, never at a human command. The index SHALL be capped at 1,000 estimated tokens (one token per four characters, rounded up). The core-prompt under-1,000-token rule SHALL continue to hold for the base composed prompt, with the skills index excluded from that count and asserted separately. `/context` SHALL report `skills: <count> (index ~<tokens> tokens)`. Extension-contributed lines SHALL be enforced in this order: the contributing extension's context budget (default 2,000 estimated tokens) applies first, dropping and logging over-budget lines, then the global index cap applies to the merged list.

#### Scenario: The index carries name, description and path

- **GIVEN** a trusted project with one valid skill
- **WHEN** the session starts
- **THEN** the composed request prompt contains the skill's name, description and its path, and the session file records the `skills` section entry

#### Scenario: An oversized index truncates deterministically with a names-only footer

- **GIVEN** a workspace whose skills exceed the index cap, spanning project, user and extension scopes
- **WHEN** the session starts
- **THEN** the index keeps project entries first, then user, then extension, each by name; the cut is at the same point on every start; and the footer reads `+N more skills (not indexed): <names>` without descriptions or paths

#### Scenario: A long description cannot eat the cap

- **GIVEN** a skill with a 1,000-character description
- **WHEN** the session starts
- **THEN** its index line carries at most 200 characters of description plus `…`

#### Scenario: The index is built once per session

- **GIVEN** a running session whose skill file changes on disk mid-run
- **WHEN** the next request is composed
- **THEN** the prompt prefix is unchanged; the change appears only after a session reload

#### Scenario: /context reports the index size

- **GIVEN** a session with a discovered skill index
- **WHEN** `/context` is submitted
- **THEN** the notice includes `skills: <count> (index ~<tokens> tokens)` and no model call happens

#### Scenario: The base prompt stays under the core cap

- **GIVEN** the repository's own workspace
- **WHEN** the system prompt is composed
- **THEN** the base prompt asserts under 1,000 tokens with the skills index excluded and counted separately

### Requirement: Skill files are readable across the workspace boundary

The read tool SHALL allow read-only access to every directory of a discovered skill — project, user (`~/.lunate/skills`, `~/.agents/skills`) and extension sources — including arbitrary files bundled under it (`references/`, `scripts/`, any file), even when the directory lies outside the run's workspace root (ADR-0016 boundary). The allowance SHALL be computed per run from that run's own discovery, so child runs in their worktrees resolve the same files.

#### Scenario: A user-scope skill and its bundled file read without a boundary refusal

- **GIVEN** a trusted session with a user skill `greek` carrying `references/long.md`
- **WHEN** the model reads the skill's `SKILL.md` and then the bundled file
- **THEN** both reads succeed like workspace reads, and the boundary check reports no violation

#### Scenario: A child run resolves its own allowance

- **GIVEN** a child run executing in a worktree of the same repository
- **WHEN** it reads an indexed user-scope skill's file
- **THEN** the read succeeds

### Requirement: The `/skills` view

`/skills` SHALL list every discovered skill as one segment per skill: `<name> (<scope>) — <description>` with optional markers `command-only`, `<extension '<id>'>` as the scope, `(not indexed)` for entries cut by index overflow, `shadows <scope>`, `(untrusted)`, and for invalid entries `(invalid) — <first sentence>`. The view SHALL read `skills: none found` when nothing is discovered, SHALL print no file contents, and SHALL never start a model call.

#### Scenario: /skills lists scopes, command-only, untrusted and overflow states

- **GIVEN** an idle session with one loaded project skill, one user `disable-model-invocation` skill, one untrusted project skill, and one project skill cut by index overflow
- **WHEN** `/skills` is submitted
- **THEN** one segment per skill appears with `(project)`, `(user, command-only)`, `(project, untrusted)` and `(project, not indexed)` respectively, and no model call happens

#### Scenario: /skills with no skills

- **GIVEN** an idle session with no skills discovered
- **WHEN** `/skills` is submitted
- **THEN** the notice reads `skills: none found` and no model call happens

### Requirement: Skill invocation via `/skill:<name>`

`/skill:<name> [args]` SHALL load the named skill's full body into the session by submitting a wrapped **user turn** through the normal prompt submission path: `<skill name="<name>" dir="<absolute skill directory>">` + body + `</skill>`, so relative references resolve and the system prompt stays byte-stable. The turn SHALL be recorded as a user session entry carrying the skill metadata (name, dir); this replaces no earlier content — a re-invocation is simply a newer turn (no stacking of prompt sections, no replace semantics). When the body contains `$ARGUMENTS`, the token SHALL be substituted with the raw argument text; otherwise non-empty arguments SHALL be appended as a final `ARGUMENTS: <args>` line. While a run is active the submission SHALL behave exactly like typed input (same queue/steer path). An unknown name SHALL produce a dim notice `unknown skill: <name> (see /skills)` with nothing submitted; an untrusted project skill SHALL be refused with a dim notice that names the fix (the trust prompt, or `lunate trust`).

#### Scenario: Invoking a skill submits the wrapped body and runs

- **GIVEN** an idle trusted session with skill `greek` discovered
- **WHEN** `/skill:greek` is submitted
- **THEN** a user turn wrapped in `<skill name="greek" dir="…">` reaches the model, the session file records it with the skill metadata, and exactly one run starts

#### Scenario: Arguments substitute into the body

- **GIVEN** a skill whose body contains `$ARGUMENTS`
- **WHEN** `/skill:<name> alpha beta` is submitted
- **THEN** the submitted text contains `alpha beta` where the token stood

#### Scenario: Unknown skill name

- **GIVEN** an idle session without a skill named `ghost`
- **WHEN** `/skill:ghost` is submitted
- **THEN** a dim notice reads `unknown skill: ghost (see /skills)`, no run starts, and nothing reaches the model

#### Scenario: Untrusted project skill refusal names the fix

- **GIVEN** an untrusted repository with a project skill `greek`
- **WHEN** `/skill:greek` is submitted
- **THEN** a dim notice refuses and names the trust prompt and `lunate trust`, no run starts, and nothing reaches the model

#### Scenario: Invocation while a run is active queues like typed input

- **GIVEN** a session with a run in progress
- **WHEN** `/skill:<name>` is submitted
- **THEN** the submission takes the same queue/steer path as typed text and no separate invocation path is used

### Requirement: Project skills trust

Project skills SHALL load only when the repository is trusted for skills. The first evaluation SHALL ask one question per repository identity listing the discovered project skill names; the decision SHALL be recorded content-hashed (directory content of the skill directories) so changed skills re-prompt. `/trust` SHALL list the skills record with the other records and `/trust clear` SHALL clear it; `lunate trust` SHALL record it non-interactively like the other items. While untrusted, project skills SHALL be excluded from the model index and listed `(untrusted)` by `/skills`.

#### Scenario: The trust question lists project skill names

- **GIVEN** an untrusted repository with project skills `greek` and `roman`
- **WHEN** a session starts
- **THEN** the question's details list `greek, roman` and approving records the decision

#### Scenario: Changed skills re-prompt

- **GIVEN** a repository trusted for skills whose skill directory content changed
- **WHEN** a session starts
- **THEN** the skills question is asked again

#### Scenario: Untrusted skills stay out of the index

- **GIVEN** an untrusted repository with a project skill
- **WHEN** the session starts
- **THEN** the model index contains no entry for it and `/skills` lists it `(untrusted)`
