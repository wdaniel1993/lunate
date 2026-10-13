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

The CLI SHALL provide a `lunate trust` command for scripted and CI runs, operating on the current working directory's repository identity and worktree. With no flags it SHALL print this repository's trust records, every pending decision — a project extension whose hash is unknown or changed, or an untrusted project `mcp.json` — using the same content the interactive trust question shows (extension id, version, directory, and the changed-files note when re-prompting; the config path with each server's name and command), and one machine-independent digest over all pending decisions; the digest SHALL cover pending content only — no repository identity, worktree path, extension directory or config path — so a digest pinned on any machine matches the same pending content anywhere; it SHALL record nothing. `--yes` SHALL print the same decisions and then record them in the store's sections (extension trust and the project `mcp.json`), including a new worktree of a trusted repository, which a session would record without asking; a second invocation SHALL find nothing pending and succeed. `--yes --digest <sha>` SHALL recompute the digest and record only on an exact match; on mismatch it SHALL record nothing and show the difference. `--json` SHALL be the inspect output contract: one JSON document carrying the records, the pending decisions and the digest, canonical newlines and invariant culture; combining it with `--yes` or `--clear` SHALL be a usage error. `--clear` SHALL clear the repository's records and print what it removed. The command SHALL perform discovery and hashing only — it SHALL NOT load extension assemblies, dispatch hooks, or start MCP servers — and the hashes it records SHALL be the hashes it prints. Exit codes SHALL be 0 for success (inspect with nothing pending), 3 when inspect finds pending decisions, 4 on a digest mismatch, 2 for usage errors (unknown or conflicting flags), and 1 for unexpected failures.

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
- **THEN** the question is asked again and the command printed what it removed

#### Scenario: Bad usage records nothing

- **GIVEN** conflicting flags such as `--yes --clear`
- **WHEN** the command runs
- **THEN** usage is printed on stderr, the exit code is 2, and nothing is recorded

#### Scenario: Inspect reports pending decisions through its exit code

- **GIVEN** an untrusted repository with a project extension
- **WHEN** `lunate trust` runs
- **THEN** the exit code is 3, and it is 0 when nothing is pending

#### Scenario: A pinned digest gates the record

- **GIVEN** an untrusted repository whose pending decisions were inspected
- **WHEN** `lunate trust --yes --digest <the printed digest>` runs
- **THEN** the records are written

#### Scenario: A mismatched digest records nothing

- **GIVEN** an untrusted repository and a digest that no longer matches the pending decisions
- **WHEN** `lunate trust --yes --digest <stale digest>` runs
- **THEN** nothing is recorded, the exit code is 4, and the pinned digest, the computed digest and the current pending decisions are shown

### Requirement: Trust command security posture

The `lunate trust` command SHALL be the only non-interactive way to record trust; no environment variable, configuration key or flag SHALL make a session record trust implicitly, and no console prompt SHALL return. The command SHALL print exactly what it records before recording it. For untrusted inputs the pinned form SHALL be the CI guidance: inspect prints a digest over the pending decisions, and `--yes --digest <sha>` records only when the recomputed digest matches — a mismatch records nothing, shows the difference, and exits non-zero. Plain `--yes` SHALL be documented as being for checkouts the operator controls, never for untrusted pull requests or forks. The project `mcp.json` trust hash SHALL be computed over each server's raw definition as written — before any `${VAR}` expansion — canonically serialized, so it is machine-independent and no expanded value or secret can influence it. Records SHALL be documented as per user and machine: ephemeral CI runners SHALL run the trust step in every job.

#### Scenario: Expanded values never enter the hash

- **GIVEN** a project `mcp.json` whose definitions reference environment variables
- **WHEN** its trust is recorded with any environment values
- **THEN** the recorded hash depends only on the raw definitions and no secret value appears in the trust file
