# skills Specification

## Purpose
Portable agent skills: project and user `SKILL.md` files discovered, indexed into the system prompt under their own budget, listed and invoked through `/skills` and `/skill:<name>`, gated by repository trust, and extensible by extension-provided skill sources.

## Requirements

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

### Requirement: Skill format and validation

A skill SHALL be a directory whose `SKILL.md` carries YAML frontmatter with a non-empty `name` and `description`; `name` SHALL equal the directory name (deterministic `/skill:` addressing) and SHALL match `[a-z0-9-]+`. The optional `allowed-tools` field SHALL be accepted and ignored — it is never a permission grant in any scope. The optional `disable-model-invocation: true` SHALL exclude the skill from the model index while keeping it invokable via `/skill:<name>`. A malformed or unreadable `SKILL.md` SHALL warn once per session and SHALL NOT load. A description longer than 1,024 characters (the Agent Skills specification limit) SHALL make the skill invalid with the same one-time warning, non-load, index absence and read-allowance exclusion — the spec limit is validation, never a silent cut.

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

#### Scenario: A description over the spec limit is an invalid skill

- **GIVEN** a trusted project skill whose `SKILL.md` description exceeds 1,024 characters
- **WHEN** the session starts
- **THEN** a warning names the directory and the 1,024-character limit, the skill is absent from the index, `/skills` lists it as `(invalid)` with the reason's first sentence, and its directory grants no read allowance

### Requirement: Model exposure and budget

Session start SHALL append the skills index to the system prompt as a section named `skills`, recorded as a prompt-section entry for deterministic replay, and SHALL build it exactly once per session: never rebuilt mid-run (the prompt prefix stays cache-stable), rebuilt only on a wholesale session reload (`/new`, resume-with-reload, extension reload). Each line SHALL read `- <name>: <description> [<path>]` — the path relative to the worktree when the skill is inside it, otherwise the canonical absolute path (directly usable by the read tool) — with the description carried whole up to the Agent Skills specification limit of 1,024 characters: the description is the trigger the model matches a task against, so it is not sentence-trimmed, and only above that limit is it sliced at 1,024 characters on a code-unit boundary that never splits a surrogate pair with a trailing `…`; a lead line SHALL state that full bodies load through the read tool. Index order SHALL be project, then user, then extension entries, each group by name (ordinal); overflow SHALL cut strictly from the end and append one footer line `+N more skills (not indexed): <names>` — names only, comma-separated, truncated at 400 characters with a trailing `…` — pointing the model at names it can act on, never at a human command. The index is no longer capped at a fixed token count: overflow begins only above the resolved hard ceiling (see the skills index budget requirement). The core-prompt under-1,000-token rule SHALL continue to hold for the base composed prompt, with the skills index excluded from that count and asserted separately. `/context` SHALL report `skills: <count> (index ~<tokens> tokens)` and advise when the complete index measures above the soft threshold. Extension-contributed lines SHALL be enforced in this order: the contributing extension's context budget (default 2,000 estimated tokens) applies first, dropping and logging over-budget lines, then the hard ceiling applies to the merged list. Above the hard ceiling the session SHALL emit a visible warning (NoticeLevel.Warning) that names how many skills lost their description; the `AGENTS.md`-named skills are indexed in full first (see the AGENTS.md priority requirement).

#### Scenario: The index carries name, description and path

- **GIVEN** a trusted project with one valid skill
- **WHEN** the session starts
- **THEN** the composed request prompt contains the skill's name, description and its path, and the session file records the `skills` section entry

#### Scenario: A first sentence is trimmed and a tail sentence is not

The scenario name carries its archived slot — the delta tooling keeps MODIFIED scenario names — and now guards the opposite: the first-sentence trim this index once applied is asserted gone.

- **GIVEN** one skill whose description is `Parses Gherkin. Also renders tables. Longer.`
- **WHEN** the session starts
- **THEN** no sentence is trimmed: the index line carries every sentence of the description whole, including the tail sentences

#### Scenario: An oversized index truncates deterministically with a names-only footer

- **GIVEN** a small-context model whose hard ceiling is exceeded by the workspace's skills, spanning project, user and extension scopes
- **WHEN** the session starts
- **THEN** the index keeps project entries first, then user, then extension, each by name; the cut is at the same point on every start; and the footer reads `+N more skills (not indexed): <names>` without descriptions or paths

#### Scenario: A long description cannot eat the cap

- **GIVEN** the composition contract (a scanned skill never reaches it above the limit because validation rejects the skill first) exercised with a raw description of 3,000 characters
- **WHEN** the index is composed
- **THEN** its index line carries at most 1,024 characters of description plus `…`

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

#### Scenario: A small-context model warns and names the count

- **GIVEN** a small-context model whose hard ceiling is crossed by the workspace's skill pack, with two skills named in `AGENTS.md`
- **WHEN** the session starts
- **THEN** a visible warning names how many skills lost their description, the `AGENTS.md`-named skills keep their full lines, and the overflowed skills are listed names-only in the footer

### Requirement: The skills index budget scales with the model

The skills index SHALL keep every model-visible skill's full index line while its estimated token count fits the resolved budgets, so a normal-size pack is never truncated by an arbitrary fixed cap. The budgets SHALL derive from the model's context window with the same precedence as the session model: an extension-registered model's declared window, else the configured catalog entry, else the 128,000-token default. The soft threshold SHALL be 2% of that window and the hard ceiling SHALL be 5%. The `skills.indexBudget` setting SHALL override both computed values: a positive integer estimated-token budget applies a single budget (fitting skills keep their line, overflow above it follows the hard-ceiling behaviour, and the context-window thresholds do not apply), and the literal `unlimited` indexes every skill at any size with no notice and no warning. The setting accepts user and project scope. Below the soft threshold the session SHALL emit no skills-index notice.

#### Scenario: 21 skills on a large-context model are all indexed without warnings

- **GIVEN** a trusted workspace with 21 valid project skills and a model whose context window is large enough that 2% of it exceeds the pack's estimated tokens
- **WHEN** the session starts
- **THEN** every skill's index line carries name, description and path, the footer is absent, and no skills-index notice or warning is emitted

#### Scenario: A pack above the soft threshold stays complete and warns once

- **GIVEN** a trusted workspace whose skill pack indexes above the soft threshold but below the hard ceiling
- **WHEN** the session starts
- **THEN** every skill keeps its full index line, a one-time notice names the index size and the soft threshold, and a later `/context` reports the index size with that advice; the prompt-section entry carries no footer

#### Scenario: The unlimited budget indexes everything in silence

- **GIVEN** a trusted workspace whose skill pack indexes above the computed hard ceiling with `skills.indexBudget` set to `unlimited`
- **WHEN** the session starts
- **THEN** every skill keeps its full index line, no footer is appended, and no skills-index notice or warning is emitted

#### Scenario: A numeric budget replaces the computed thresholds

- **GIVEN** a trusted workspace with a large context-window model and `skills.indexBudget` set to a positive integer below the pack's estimated tokens
- **WHEN** the session starts
- **THEN** the index overflows at that budget with the hard-ceiling behaviour — the names-only footer plus the overflow warning — and the 2%/5% context-window thresholds are not applied

### Requirement: AGENTS.md skills are indexed in full

When the hard ceiling cuts the index, a skill whose name appears in the workspace chain's `AGENTS.md` text SHALL always keep its full index line — the cut SHALL skip it, first dropping every unprotected skill and protecting the `AGENTS.md`-named ones. When every eligible skill is `AGENTS.md`-named and still over the ceiling, the index keeps all of them with their descriptions (the ceiling's flood protection yields to the explicit naming).

#### Scenario: AGENTS.md-named skills survive a ceiling cut

- **GIVEN** a small-context model whose hard ceiling forces a cut, and an `AGENTS.md` naming two of the eligible skills
- **WHEN** the session starts
- **THEN** those two skills keep their full index lines, the remaining overflowed skills move to the names-only footer, and the footer is truncated at the same point on every start

### Requirement: The soft threshold notices and advises, never truncates

Above the soft threshold the index SHALL remain complete — the threshold only informs: session start SHALL emit a one-time notice per composition that names the index size and suggests disabling unused skills or packages, and `/context` SHALL report the index size with the same suggestion as long as the index fits below the hard ceiling.

#### Scenario: /context advises at an oversized but complete index

- **GIVEN** a session whose skills index is complete but above the soft threshold
- **WHEN** `/context` is submitted
- **THEN** the notice includes the skills count and index size and a suggestion to disable unused skills or packages, and no model call happens

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

`/skills` SHALL list every discovered skill as one segment per skill: `<name> (<scope>) — <description>` with optional markers `command-only`, `<extension '<id>'>` as the scope, `(not indexed)` for entries cut by index overflow, `shadows <scope>`, `(untrusted)`, `from <package>@<version>` for a skill that arrived through `lunate install`, and for invalid entries `(invalid) — <first sentence>`. The view SHALL read `skills: none found` when nothing is discovered, SHALL print no file contents, and SHALL never start a model call.

#### Scenario: /skills lists scopes, command-only, untrusted and overflow states

- **GIVEN** an idle session with one loaded project skill, one user `disable-model-invocation` skill, one untrusted project skill, and one project skill cut by index overflow
- **WHEN** `/skills` is submitted
- **THEN** one segment per skill appears with `(project)`, `(user, command-only)`, `(project, untrusted)` and `(project, not indexed)` respectively, and no model call happens

#### Scenario: /skills marks a package's skill with its provenance

- **GIVEN** an idle session with a user skill installed from `Acme.Toolkit@1.4.2`
- **WHEN** `/skills` is submitted
- **THEN** the line for that skill carries `from Acme.Toolkit@1.4.2` after the scope

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

Project skills SHALL load only when the repository is trusted for skills. The first evaluation SHALL ask one question per repository identity listing the discovered project skill names; the decision SHALL be recorded content-hashed (directory content of the skill directories) so changed skills re-prompt. For a skill that arrived through `lunate install`, the question SHALL name the source package on one line, `from package <id>@<version> (<source>)`, and the recorded decision SHALL bind that package identity — canonical id, exact version and the nupkg's SHA-512 — beside the directory content hash, so an upgrade re-prompts. `/trust` SHALL list the skills record with the other records and `/trust clear` SHALL clear it; `lunate trust` SHALL record it non-interactively like the other items. While untrusted, project skills SHALL be excluded from the model index and listed `(untrusted)` by `/skills`.

#### Scenario: The trust question lists project skill names

- **GIVEN** an untrusted repository with project skills `greek` and `roman`
- **WHEN** a session starts
- **THEN** the question's details list `greek, roman` and approving records the decision

#### Scenario: Changed skills re-prompt

- **GIVEN** a repository trusted for skills whose skill directory content changed
- **WHEN** a session starts
- **THEN** the question is asked again

#### Scenario: Untrusted skills stay out of the index

- **GIVEN** an untrusted repository with a project skill
- **WHEN** the session starts
- **THEN** the model index contains no entry for it and `/skills` lists it `(untrusted)`

#### Scenario: An installed skill's question names its package

- **GIVEN** an untrusted repository whose only project skill came from an install of `Acme.Toolkit@1.4.2`
- **WHEN** the skills question renders
- **THEN** it lists the skill name and one line reads `from package Acme.Toolkit@1.4.2 (nuget.org)`

#### Scenario: An upgraded package re-prompts for its skills

- **GIVEN** a repository trusted for the skills of `Acme.Toolkit@1.4.2`
- **WHEN** `lunate install Acme.Toolkit@1.5.0 --project` runs and a session starts
- **THEN** the skills question is asked again
