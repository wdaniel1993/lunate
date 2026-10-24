## Purpose

A remote extension's manifest commands become runnable: one request method on the existing wire, the in-process lease and notice semantics carried over unchanged, and a capability flag so a child that cannot serve commands is never offered as if it could.

## MODIFIED Requirements

### Requirement: Extension commands

The extension contract SHALL let an extension register command sources through `IExtensionContext.RegisterCommandSource` at creation time only, with no processes, sockets or timers started by registration. A manifest `commands` entry SHALL be a declaration of an extension command; a command SHALL be runnable only when a registered source provides it and the manifest declares it. A provided command that the manifest does not declare SHALL refuse the load. A declared command that no registered source provides SHALL be logged and shown by `/extensions` as `declared, not provided`, and SHALL NOT appear in autocomplete or dispatch. A provided command whose name equals a built-in command SHALL refuse the load with a message naming the extension and the built-in. An invalid provided name (empty, whitespace or a leading slash) SHALL refuse the load naming the field. Described commands SHALL be cached at load, re-stamped with the extension id as source and the `Extension` kind, and removed with the extension on unload. An invocation SHALL be async and cancellable; a throwing handler SHALL become an error notice plus a log entry, never a crash. Remote extensions SHALL provide their declared commands over the wire when the child advertises the `commands` capability (see *Remote command invocation*); a remote child that does not advertise it SHALL have every declared command reported as declared, not provided, and SHALL omit them from autocomplete and dispatch. The new context method SHALL move the extension API version as a semver-minor addition. An extension command's id SHALL be `ext:<extension id>:<bare name>`, stamped at load beside the source id and the `Extension` kind, and SHALL be the identity every diagnostic, log line and failure notice uses, so two extensions declaring one name stay distinguishable even though the bare name still dispatches to the first entry after ordering. An invocation SHALL hold a lease on its extension for its duration: unloading an extension with a live invocation SHALL cancel that invocation's token, SHALL report it to the user as a notice naming the extension and the command id, and SHALL wait a bounded time for the handler to return before the load context unloads; a handler that ignores cancellation SHALL be logged once and SHALL NOT hold the unload open beyond that bound. An unload SHALL NOT be reported as complete while a handler it cancelled is still running.

#### Scenario: A declared and provided extension command dispatches

- **GIVEN** an extension whose manifest declares `ping` and whose source provides `ping`
- **WHEN** `/ping hello` is submitted in a composed session
- **THEN** the extension handler receives `ping` with argument `hello`, the result is shown as a notice, and no model call is made

#### Scenario: A declared but unprovided command is not runnable

- **GIVEN** an extension whose manifest declares `ghost` and whose code registers no source providing it
- **WHEN** the session composes and `/extensions` is submitted
- **THEN** the load succeeds, the log names `ghost`, `/extensions` reads `declared, not provided`, `/` does not offer it, and submitting it produces the unknown-command notice

#### Scenario: A built-in name collision is refused at load

- **GIVEN** an extension providing a command named `help` while `help` is a built-in command
- **WHEN** the extension loads
- **THEN** the load is refused with a message naming the extension and the built-in command

#### Scenario: A provided but undeclared command refuses the load

- **GIVEN** an extension whose source provides `surprise` while the manifest declares no such command
- **WHEN** the extension loads
- **THEN** the load is refused with a message naming the extension, the command and the manifest rule

#### Scenario: Unload removes extension commands

- **GIVEN** a loaded extension contributing a command
- **WHEN** the extension unloads
- **THEN** the command leaves the registry, autocomplete and dispatch

#### Scenario: A throwing extension handler is contained

- **GIVEN** a loaded extension command whose handler throws
- **WHEN** the command is submitted
- **THEN** the session shows an error notice, the log names the extension and command, and later commands still work

#### Scenario: Two extensions may declare the same command name

- **GIVEN** extensions `alpha` and `beta` whose sources both provide `ping`
- **WHEN** the registry composes and `/ping` is submitted
- **THEN** both extensions load, `/ping` dispatches to `alpha` (first after ordering), and the log records the shadowed `ext:beta:ping`

#### Scenario: Unload cancels an in-flight command

- **GIVEN** a loaded extension command whose handler awaits its token
- **WHEN** the extension is unloaded while the command runs
- **THEN** the handler observes cancellation, the session shows a notice naming the extension and the command id, and the unload returns after the handler leaves

#### Scenario: A handler that ignores cancellation does not block unload

- **GIVEN** an extension command handler that swallows its cancellation token
- **WHEN** the extension is unloaded while the command runs
- **THEN** the log records the unresponsive handler with its command id and the unload returns inside the bound

## ADDED Requirements

### Requirement: Remote command invocation

The wire SHALL carry a `command/invoke` request from host to child, and the child SHALL answer it with a result or a failure. The payload SHALL be a wire DTO owned by the remote host package carrying the bare command name, the argument remainder and the invocation's request id — never a serialisation of the public invocation or result types — and the reply SHALL mirror exactly the output text, the optional failure message and the disposition of the in-process result. A field added to the abstract type SHALL require the DTO and the conformance run to move with it.

Cancellation SHALL reuse the existing `$/cancelRequest` with the invoke request id; no second cancel path SHALL exist. The child SHALL pass that cancellation to the handler's token, and a cancelled invocation SHALL reply as cancelled rather than failed.

An invocation SHALL hold the same lease on the extension as an in-process one: unloading cancels it, the cancel is reported to the user as a notice naming the extension and the command id, and the unload waits a bounded time for the reply. A throwing or failed remote handler SHALL become the same error notice shape as an in-process failure, naming the source-qualified command id, and SHALL NOT crash the session.

The child SHALL advertise a `commands` capability in `initialize`. A child that does not advertise it SHALL have its declared commands treated as declared-not-provided, omitted from autocomplete and dispatch. Adding the method SHALL raise the wire protocol version to 2; a version-1 child SHALL remain readable for hooks and services.

#### Scenario: A manifest command completes over the wire

- **GIVEN** a remote extension whose manifest declares `ping` and whose child advertises the `commands` capability
- **WHEN** the user submits `/ping hello`
- **THEN** the child receives the bare name `ping` with the remainder `hello`, its reply reaches the transcript as the command's output, and the notice names `ext:<id>:ping`

#### Scenario: Cancel mid-invocation is honoured across the wire

- **GIVEN** a remote command handler awaiting its token
- **WHEN** the invocation is cancelled
- **THEN** the child observes cancellation, the reply is `cancelled`, and no error notice is rendered

#### Scenario: A failing remote handler matches the in-process notice shape

- **GIVEN** a remote command handler that throws
- **WHEN** the invocation runs
- **THEN** the notice is the same shape as an in-process failure, names the source-qualified id, and the session continues

#### Scenario: A child without the capability is not offered its commands

- **GIVEN** a version-1 remote child that declares `ghost`
- **WHEN** the composition lists commands
- **THEN** `ghost` is absent from autocomplete and `/help`, and `/extensions` reports it as declared, not provided

#### Scenario: Unload during an invocation cancels it

- **GIVEN** a remote invocation in flight
- **WHEN** the extension is unloaded
- **THEN** the invocation's token is cancelled, a notice names the extension and command id, and the unload completes within its bound
