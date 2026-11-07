## ADDED Requirements

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

## MODIFIED Requirements

### Requirement: Model exposure and budget

Session start SHALL append the skills index to the system prompt as a section named `skills`, recorded as a prompt-section entry for deterministic replay, and SHALL build it exactly once per session: never rebuilt mid-run (the prompt prefix stays cache-stable), rebuilt only on a wholesale session reload (`/new`, resume-with-reload, extension reload). Each line SHALL read `- <name>: <description> [<path>]` — the path relative to the worktree when the skill is inside it, otherwise the canonical absolute path (directly usable by the read tool) — with descriptions trimmed to their first sentence and truncated at 200 characters plus `…` on a code-unit boundary that never splits a surrogate pair (a sentence whose first sentence ends later keeps the description capped at 200 characters); a lead line SHALL state that full bodies load through the read tool. Index order SHALL be project, then user, then extension entries, each group by name (ordinal); overflow SHALL cut strictly from the end and append one footer line `+N more skills (not indexed): <names>` — names only, comma-separated, truncated at 400 characters with a trailing `…` — pointing the model at names it can act on, never at a human command. The index is no longer capped at a fixed token count: overflow begins only above the resolved hard ceiling (see the skills index budget requirement). The core-prompt under-1,000-token rule SHALL continue to hold for the base composed prompt, with the skills index excluded from that count and asserted separately. `/context` SHALL report `skills: <count> (index ~<tokens> tokens)` and advise when the complete index measures above the soft threshold. Extension-contributed lines SHALL be enforced in this order: the contributing extension's context budget (default 2,000 estimated tokens) applies first, dropping and logging over-budget lines, then the hard ceiling applies to the merged list. Above the hard ceiling the session SHALL emit a visible warning (NoticeLevel.Warning) that names how many skills lost their description; the `AGENTS.md`-named skills are indexed in full first (see the AGENTS.md priority requirement).

#### Scenario: The index carries name, description and path

- **GIVEN** a trusted project with one valid skill
- **WHEN** the session starts
- **THEN** the composed request prompt contains the skill's name, description and its path, and the session file records the `skills` section entry

#### Scenario: A first sentence is trimmed and a tail sentence is not

- **GIVEN** one skill whose description is `Parses Gherkin. Also renders tables. Longer.` and another whose description has no sentence boundary before 200 characters
- **WHEN** the session starts
- **THEN** the first skill's index line carries only its first sentence and the second carries exactly its first 200 characters plus `…`

#### Scenario: An oversized index truncates deterministically with a names-only footer

- **GIVEN** a small-context model whose hard ceiling is exceeded by the workspace's skills, spanning project, user and extension scopes
- **WHEN** the session starts
- **THEN** the index keeps project entries first, then user, then extension, each by name; the cut is at the same point on every start; and the footer reads `+N more skills (not indexed): <names>` without descriptions or paths

#### Scenario: A long description cannot eat the cap

- **GIVEN** a skill with a 1,000-character description without a sentence boundary in its first 200 characters
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

#### Scenario: A small-context model warns and names the count

- **GIVEN** a small-context model whose hard ceiling is crossed by the workspace's skill pack, with two skills named in `AGENTS.md`
- **WHEN** the session starts
- **THEN** a visible warning names how many skills lost their description, the `AGENTS.md`-named skills keep their full lines, and the overflowed skills are listed names-only in the footer
