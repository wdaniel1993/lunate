## MODIFIED Requirements

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
