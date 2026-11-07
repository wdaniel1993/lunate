## MODIFIED Requirements

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
