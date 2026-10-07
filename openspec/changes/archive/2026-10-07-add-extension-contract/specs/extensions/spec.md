## ADDED Requirements

### Requirement: Extension contract assembly
Extensions SHALL compile against `Lunate.Extensibility.Abstractions` and `Microsoft.Extensions.AI.Abstractions` only — never against `Lunate.Agent` internals. The contract assembly SHALL be semver'd with a PublicAPI-tracked surface, and its entry point SHALL be `IExtensionFactory.Create(IExtensionContext)` returning an `IExtension`. The context SHALL expose the extension id, settings, secrets and a log sink. The current API version SHALL be a single constant, and the assembly SHALL depend on no packages beyond the MEAI abstractions.

#### Scenario: An extension compiles against the contract only
- **GIVEN** an extension project referencing the abstractions and MEAI abstractions
- **WHEN** it is built
- **THEN** it compiles without any reference to `Lunate.Agent`

#### Scenario: The API version is queryable
- **GIVEN** the contract assembly
- **WHEN** the current API version is read
- **THEN** it is a single semver constant that the range matcher validates against

### Requirement: Extension manifest
An extension SHALL declare an `extension.json` manifest with `id` (lowercase, `[a-z0-9-]+`), `version`, `apiVersion` (a range), `entryAssembly` and optional `tools`, `commands`, `hooks`, `services`, `settingsSchema` and `capabilities`. Unknown top-level fields SHALL be ignored; malformed known fields SHALL fail with an error naming the file and the field; duplicate declarations SHALL be rejected. The API-version range grammar SHALL be exact `x.y.z` or caret `^x.y.z`; other forms SHALL be refused as unsupported. An incompatible range SHALL be refused with a message naming the extension, its range and the current API version.

#### Scenario: A valid manifest parses
- **GIVEN** a manifest with all required fields
- **WHEN** it is parsed
- **THEN** the parsed model carries every declared field and defaults the optional arrays to empty

#### Scenario: Unknown fields are forward-compatible
- **GIVEN** a manifest with an unknown top-level field
- **WHEN** it is parsed
- **THEN** it parses successfully and the unknown field is ignored

#### Scenario: An incompatible range is refused
- **GIVEN** a manifest whose `apiVersion` excludes the current API version
- **WHEN** it is validated
- **THEN** the refusal names the extension, its range and the current API version

### Requirement: Extension loading
The host SHALL discover extensions in `~/.lunate/extensions/` (global) and `.lunate/extensions/` (project), reading manifests only — assemblies SHALL load lazily on first use into a collectible `AssemblyLoadContext` that shares the abstractions, `Microsoft.Extensions.AI.Abstractions` and `System.Text.Json` with the default context and isolates every other dependency. Unload SHALL be idempotent; a later load SHALL work; state SHALL not survive. Duplicate ids across scopes SHALL be an error naming both paths. Missing dependencies SHALL yield an actionable error.

#### Scenario: Manifests are read without loading assemblies
- **GIVEN** an installed extension
- **WHEN** discovery runs
- **THEN** its manifest is read and its assembly is not loaded

#### Scenario: A loaded extension unloads and reloads
- **GIVEN** a loaded extension
- **WHEN** it is unloaded and loaded again
- **THEN** both operations succeed and the second instance is fresh

#### Scenario: A missing dependency fails actionably
- **GIVEN** an extension whose entry assembly requires an assembly that is not present
- **WHEN** it is loaded
- **THEN** loading fails with an error naming the missing assembly and the extension

### Requirement: Extension settings and secrets
Per-extension settings SHALL be read from the host store and validated against a documented subset of the manifest's `settingsSchema` (top-level `required` and one-level `type` checks), with violations naming the extension and the field. Secrets SHALL be namespaced per extension, exposed read-only, and SHALL never be written to session files or logs.

#### Scenario: A settings violation is actionable
- **GIVEN** a settings value violating the schema subset
- **WHEN** the extension is loaded
- **THEN** loading fails naming the extension and the field

#### Scenario: Secrets stay out of sessions
- **GIVEN** a loaded extension whose secrets are read
- **WHEN** a session is written during that run
- **THEN** no secret value appears in the session file

### Requirement: Project extension trust
Project extensions SHALL require a one-time trust decision per repository identity (host-provided, `GitCommonDir` per the worktree architecture), re-prompted when the extension content hash changes for a worktree. New worktrees of a trusted repository SHALL not re-prompt. Denial SHALL refuse the load. Global extensions SHALL not be gated. Content hashes SHALL be computed over the extension directory's files deterministically, and trust records SHALL be written atomically.

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
