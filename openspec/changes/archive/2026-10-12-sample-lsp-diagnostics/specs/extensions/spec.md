## MODIFIED Requirements

### Requirement: Hook wiring

Hooks SHALL fire where their producers exist: `SessionStarted`/`SessionEnding` (loader lifecycle; started safe to run more than once, ending idempotent), `RunStarting`, `ContextBuilding`, `ProviderStreamEvent`, `MessageCompleted`, `ToolCalling` (before the approval prompt, with the resolved tool's annotations — lowercase kebab wire names in declaration order: `read-only`, `destructive`, `idempotent`, `open-world` — on the payload so policy handlers can decide on them), `ToolResultReady` (composing in order; the payload carries the call's arguments as approved, and the result may replace the output, attach model-facing structured content, and set UI-only details), `TurnEnded`, `RunSettled`, `ProjectTrust` (global-extension handlers in the trust flow), and `Compacting` (before compaction; the last provided summary wins, and handler failure falls back to the default summarization). Hooks without producers yet (`InputReceived`) SHALL be contract-complete and documented as unwired. The harness SHALL NOT depend on the extension system: it exposes no-op seam points, and the host adapter maps to the hook DTOs.

#### Scenario: ToolCalling runs before approval

- **GIVEN** a ToolCalling handler mutating arguments
- **WHEN** a tool call is approved
- **THEN** the user approves the final (mutated) arguments

#### Scenario: The ToolCalling payload carries the tool's annotations

- **GIVEN** a tool declared with annotations (for example `Destructive`)
- **WHEN** a call to it reaches the ToolCalling hook
- **THEN** the payload lists the tool's annotations, and a policy handler can block the call or let it fall through to the approval prompt on that basis

#### Scenario: A provided summary overrides the default

- **GIVEN** a Compacting handler returning `Provide` with a summary
- **WHEN** compaction runs
- **THEN** the provided summary is recorded instead of the default summarization

#### Scenario: The harness stays extension-agnostic

- **GIVEN** a harness configured without hooks
- **WHEN** it runs
- **THEN** behavior is unchanged

#### Scenario: The ToolResultReady payload carries the call's arguments

- **GIVEN** a ToolResultReady handler
- **WHEN** a tool call completes
- **THEN** the payload carries the call's arguments as approved, and a handler returning structured content sees it on the tool result

### Requirement: File change bus

The core SHALL emit a `FileChanged` notification (canonical path + workspace id) after every successful file mutation performed by the `write` and `edit` tools; refused or failed mutations SHALL emit nothing. Extensions SHALL subscribe with a handler interface and an optional `*`/`?` pattern filter. Delivery SHALL be ordered per workspace and SHALL never throw into the mutation path; handler failures follow the hook failure policy (report). The workspace id SHALL be the run's `WorktreeRoot`. The mutation queue integration SHALL deliver exactly one event per successful mutation. **Not yet wired in production** (tracked separately): only the test kit registers `core/file-bus` today, so production sessions emit nothing until that wiring lands.

#### Scenario: Events after write and edit

- **GIVEN** a subscribed extension
- **WHEN** `write` and `edit` mutate files successfully
- **THEN** one event per mutation arrives with the canonical path and the workspace id

#### Scenario: No event for a refused mutation

- **GIVEN** an `edit` that is refused (no match or ambiguous)
- **WHEN** it returns
- **THEN** no event is emitted

#### Scenario: Ordering under the queue

- **GIVEN** concurrent queued mutations on one file
- **WHEN** they complete
- **THEN** events arrive in the same order as the mutations applied

### Requirement: Service registry

Extensions SHALL register named services and look them up by name; duplicate names SHALL be refused with an error naming both extensions; reserved core service names SHALL not be shadowable. Core services SHALL be exposed to extensions as documented read-only handles (file bus subscription, mutation queue scheduling, workspace identity). The host SHALL register the workspace identity handle (`core/workspace`) when it composes a session — production sessions and the test kit alike — before any extension loads.

#### Scenario: Duplicate service names are refused

- **GIVEN** two extensions registering the same service name
- **WHEN** the second registers
- **THEN** registration fails naming both extensions

#### Scenario: Core services are reachable

- **GIVEN** an extension
- **WHEN** it looks up a core service by its reserved name
- **THEN** it receives the documented handle

#### Scenario: Workspace identity is reachable in a composed session

- **GIVEN** a composed session
- **WHEN** an extension looks up `core/workspace`
- **THEN** it receives the session's workspace identity (worktree root, repo root, git common dir)
