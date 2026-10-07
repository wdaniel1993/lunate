## ADDED Requirements

### Requirement: Background services
Extensions SHALL register background services (`IBackgroundService` with `StartAsync`/`StopAsync`) at creation time. The host SHALL start them with `SessionStarted` semantics (safe to run more than once; started services are not restarted) and stop them idempotently on `SessionEnding`; unloading SHALL drop them. A start failure SHALL be reported with the extension id and service name, SHALL prevent that extension's remaining unstarted services from starting, and SHALL stop its already-started services best-effort; stop failures SHALL be reported and never thrown to the lifecycle caller.

#### Scenario: A service starts and stops with the session
- **GIVEN** an extension with a registered service
- **WHEN** the session starts and ends
- **THEN** the service starts once and stops once, idempotently on repeat

#### Scenario: A failing start stops the extension's remaining services
- **GIVEN** an extension whose first service throws on start
- **WHEN** the session starts
- **THEN** the failure is reported and the remaining services do not start

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
Extensions SHALL register named services and look them up by name; duplicate names SHALL be refused with an error naming both extensions; reserved core service names SHALL not be shadowable. Core services SHALL be exposed to extensions as documented read-only handles (file bus subscription, mutation queue scheduling, workspace identity).

#### Scenario: Duplicate service names are refused
- **GIVEN** two extensions registering the same service name
- **WHEN** the second registers
- **THEN** registration fails naming both extensions

#### Scenario: Core services are reachable
- **GIVEN** an extension
- **WHEN** it looks up a core service by its reserved name
- **THEN** it receives the documented handle

### Requirement: Model provider registry
Extensions SHALL declare model providers in the manifest (`id`, `displayName`, `endpoint`, `secretName`, `modelIds`). Declarations SHALL be validated (namespaced id, absolute http(s) endpoint, non-empty model ids, `secretName` referencing the secrets store by name — never a value); duplicate provider ids SHALL be refused. The registry SHALL list declared providers; instantiating clients and routing land with the model-router card.

#### Scenario: A declared provider is listed
- **GIVEN** a manifest declaring a valid model provider
- **WHEN** the extension loads
- **THEN** the registry lists it with its display name, endpoint and model ids

#### Scenario: Secret values never appear
- **GIVEN** a provider declaration with a `secretName`
- **WHEN** the provider is listed or logged
- **THEN** only the name appears, never the secret value

#### Scenario: Duplicate provider ids are refused
- **GIVEN** two extensions declaring the same provider id
- **WHEN** the second loads
- **THEN** loading fails naming both extensions
