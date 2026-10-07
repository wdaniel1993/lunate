# extensions Specification

## Purpose

The extension system: a small, semver'd contract assembly that extensions compile against, manifest-driven discovery and lazy loading into collectible load contexts, per-extension settings and secrets, and one-time project trust keyed by repository identity.

## Requirements

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

### Requirement: Hook contract
The contract assembly SHALL define typed payload/result DTOs and handler interfaces for every hook in the catalogue (`ProjectTrust`, `SessionStarted`, `SessionEnding`, `InputReceived`, `RunStarting`, `ContextBuilding`, `ProviderStreamEvent`, `MessageCompleted`, `ToolCalling`, `ToolResultReady`, `TurnEnded`/`RunSettling`, `RunSettled`, `Compacting`, `ModelChanged`/`ToolsChanged`). Every payload and result SHALL round-trip through `System.Text.Json` (conformance-tested). Extensions SHALL register handlers through `IExtensionContext.Register` at creation time only — registration SHALL NOT start processes, sockets or timers. The API version SHALL move as a semver-minor addition.

#### Scenario: Every hook DTO round-trips through JSON
- **GIVEN** a payload or result DTO of any catalogue hook
- **WHEN** it is serialized and deserialized
- **THEN** the values are identical

#### Scenario: Registration is registration only
- **GIVEN** an extension registering handlers
- **WHEN** its factory runs
- **THEN** no processes, sockets or timers are started

### Requirement: Hook dispatch semantics
Handlers SHALL run by priority descending with a stable load-order tie-break (higher priority first; ties keep load order). Each hook's semantics class SHALL be enforced: observe handlers all run with results ignored; transform handlers chain in order (each sees the previous output, final value used); replace handlers chain and the final value is persisted; block handlers stop at the first block (final mutated arguments are what the user approves). Ordering SHALL be deterministic.

#### Scenario: Order is deterministic
- **GIVEN** handlers from several extensions with and without priorities
- **WHEN** a hook dispatches
- **THEN** handlers run by priority descending with a stable load-order tie-break, across runs

#### Scenario: Transforms chain
- **GIVEN** two transform handlers
- **WHEN** a hook dispatches
- **THEN** the second sees the first's output and the final value is used

#### Scenario: The first block wins
- **GIVEN** two blocking handlers
- **WHEN** a hook dispatches
- **THEN** the chain stops at the first block and its reason is surfaced

### Requirement: Hook failure policy
Every hook SHALL have a declared failure policy per the catalogue (report / skip / keep original / fail-safe block / deny / fall back), applied on handler exception and on timeout. A failing handler SHALL never crash the run; the failure SHALL be reported through the host log sink. `ToolCalling` failures (throw or timeout) SHALL block fail-safe; `ProjectTrust` failures SHALL deny.

#### Scenario: A throwing ToolCalling handler blocks
- **GIVEN** a ToolCalling handler that throws or times out
- **WHEN** the tool call dispatches
- **THEN** the call is blocked with a reason and the failure is reported

#### Scenario: A throwing transform handler keeps the original
- **GIVEN** a transform handler that throws
- **WHEN** the hook dispatches
- **THEN** the original value is used and the failure is reported

### Requirement: Timeouts and continuation caps
Each handler SHALL run under a per-handler timeout (configurable; `ProviderStreamEvent` on a shorter fast path). Continuations requested at turn boundaries SHALL be capped per run (default 3); requests beyond the cap SHALL be ignored and logged.

#### Scenario: A hanging handler is cut off by policy
- **GIVEN** a handler that never completes
- **WHEN** its timeout elapses
- **THEN** its hook's failure policy applies

#### Scenario: Continuation requests are capped
- **GIVEN** a run whose turn-boundary handlers keep requesting continuations
- **WHEN** the cap is reached
- **THEN** further requests are ignored and logged

### Requirement: Hook wiring
Hooks SHALL fire where their producers exist: `SessionStarted`/`SessionEnding` (loader lifecycle; started safe to run more than once, ending idempotent), `RunStarting`, `ContextBuilding`, `ProviderStreamEvent`, `MessageCompleted`, `ToolCalling` (before the approval prompt), `ToolResultReady` (composing in order), `TurnEnded`, `RunSettled`, and `ProjectTrust` (global-extension handlers in the trust flow). Hooks without producers yet (`InputReceived`, `Compacting`) SHALL be contract-complete and documented as unwired. The harness SHALL NOT depend on the extension system: it exposes no-op seam points, and the host adapter maps to the hook DTOs.

#### Scenario: ToolCalling runs before approval
- **GIVEN** a ToolCalling handler mutating arguments
- **WHEN** a tool call is approved
- **THEN** the user approves the final (mutated) arguments

#### Scenario: The harness stays extension-agnostic
- **GIVEN** a harness configured without hooks
- **WHEN** it runs
- **THEN** behavior is unchanged

### Requirement: Extension context tagging
Context added by extensions SHALL be tagged with its source extension id and SHALL respect a per-extension token budget; over-budget additions SHALL be dropped and logged.

#### Scenario: Over-budget additions are dropped
- **GIVEN** an extension adding context beyond its budget
- **WHEN** the context is merged
- **THEN** the excess is dropped and logged with the extension id
