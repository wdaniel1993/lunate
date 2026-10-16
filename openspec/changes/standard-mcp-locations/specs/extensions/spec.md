## MODIFIED Requirements

### Requirement: The `lunate trust` command

The CLI SHALL provide a `lunate trust` command for scripted and CI runs, operating on the current working directory's repository identity and worktree. With no flags it SHALL print this repository's trust records, every pending decision — a project extension whose hash is unknown or changed, an untrusted project `mcp.json` file (any of the standard project locations), or a project-scoped server definition without its per-server approval — using the same content the interactive trust question shows (extension id, version, directory, and the changed-files note when re-prompting; for a config file the path with each server's name and command; for a per-server approval the server's name, source file and full command), and one machine-independent digest over all pending decisions; the digest SHALL cover pending content only — no repository identity, worktree path, extension directory or config path — so a digest pinned on any machine matches the same pending content anywhere; it SHALL record nothing. `--yes` SHALL print the same decisions and then record them in the store's sections (extension trust, the project `mcp.json` files and the per-server approvals), including a new worktree of a trusted repository, which a session would record without asking; a second invocation SHALL find nothing pending and succeed. `--yes --digest <sha>` SHALL recompute the digest and record only on an exact match; on mismatch it SHALL record nothing and show the difference. A pinned digest SHALL require a pending set to verify: with nothing pending it SHALL record nothing and exit 4, while unpinned `--yes` with nothing pending SHALL still succeed. `--json` SHALL be the inspect output contract: one JSON document carrying the records, the pending decisions and the digest, canonical newlines and invariant culture; combining it with `--yes` or `--clear` SHALL be a usage error. `--clear` SHALL clear the repository's records — the file records and the per-server approvals for definitions present in its project files — and print what it removed. The command SHALL perform discovery and hashing only — it SHALL NOT load extension assemblies, dispatch hooks, or start MCP servers — and the hashes it records SHALL be the hashes it prints. Exit codes SHALL be 0 for success (inspect with nothing pending), 3 when inspect finds pending decisions, 4 on a digest mismatch, 2 for usage errors (unknown or conflicting flags), and 1 for unexpected failures.

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

#### Scenario: A pinned digest with nothing pending cannot be verified

- **GIVEN** a repository with no pending decisions
- **WHEN** `lunate trust --yes --digest <sha>` runs
- **THEN** nothing is recorded, the pinned digest is shown as unverifiable, and the exit code is 4

#### Scenario: Per-server approvals are pending decisions too

- **GIVEN** a trusted project file whose servers have no per-server approvals
- **WHEN** `lunate trust` runs and then `lunate trust --yes`
- **THEN** inspect prints each approval with the server's name, source file and full command and exits 3, and `--yes` records them so a later session asks nothing

### Requirement: Trust command security posture

The `lunate trust` command SHALL be the only non-interactive way to record trust; no environment variable, configuration key or flag SHALL make a session record trust implicitly, and no console prompt SHALL return. The command SHALL print exactly what it records before recording it. For untrusted inputs the pinned form SHALL be the CI guidance: inspect prints a digest over the pending decisions, and `--yes --digest <sha>` records only when the recomputed digest matches — a mismatch records nothing, shows the difference, and exits non-zero. Plain `--yes` SHALL be documented as being for checkouts the operator controls, never for untrusted pull requests or forks. The project `mcp.json` trust hash SHALL be computed over each server's raw definition as written — before any `${VAR}` expansion — canonically serialized, so it is machine-independent and no expanded value or secret can influence it. Per-server approval records SHALL follow the same discipline — recorded only by an interactive prompt or the command, keyed by the same canonical definition hash, never influenced by expanded values. Records SHALL be documented as per user and machine: ephemeral CI runners SHALL run the trust step in every job.

#### Scenario: Expanded values never enter the hash

- **GIVEN** a project `mcp.json` whose definitions reference environment variables
- **WHEN** its trust is recorded with any environment values
- **THEN** the recorded hash depends only on the raw definitions and no secret value appears in the trust file
