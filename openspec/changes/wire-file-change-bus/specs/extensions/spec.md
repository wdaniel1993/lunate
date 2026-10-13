## MODIFIED Requirements

### Requirement: File change bus

The core SHALL emit a `FileChanged` notification (canonical path + workspace id) after every successful file mutation performed by the `write` and `edit` tools; refused or failed mutations SHALL emit nothing. Extensions SHALL subscribe with a handler interface and an optional `*`/`?` pattern filter. Delivery SHALL be ordered per workspace and SHALL never throw into the mutation path; handler failures follow the hook failure policy (report). The workspace id SHALL be the run's `WorktreeRoot`. The mutation queue integration SHALL deliver exactly one event per successful mutation.

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

Extensions SHALL register named services and look them up by name; duplicate names SHALL be refused with an error naming both extensions; reserved core service names SHALL not be shadowable. Core services SHALL be exposed to extensions as documented read-only handles (file bus subscription, mutation queue scheduling, workspace identity). The host SHALL register the core handles — the workspace identity (`core/workspace`), the file change bus (`core/file-bus`) and the mutation queue (`core/mutation-queue`) — when it composes a session, production sessions and the test kit alike, before any extension loads.

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

#### Scenario: The file bus and mutation queue are live in a composed session

- **GIVEN** a composed session and an extension subscribed through `core/file-bus`
- **WHEN** a `write` mutates a matching file
- **THEN** the subscription receives the event, and `core/mutation-queue` schedules a read-modify-write that serializes with the core's own writes
