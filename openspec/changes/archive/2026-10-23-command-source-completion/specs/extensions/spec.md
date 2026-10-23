## ADDED Requirements

### Requirement: Template command source slot

The registry SHALL admit a prompt-template source without a second seam. A source describing `CommandKind.Template` descriptors SHALL compose like every other source, SHALL order after extension commands, SHALL describe ids as `template:<name>`, and SHALL appear in `/help` under `templates`. A template command whose typed name equals a built-in command SHALL lose the duplicate rule, SHALL not be listed or dispatched, and the diagnostic SHALL name both the built-in id and the template id, so a template can never shadow a built-in. This requirement SHALL NOT be read as shipping template discovery: the slot is exercised by a test source only, and a template source SHALL add no public type to the extension contract.

#### Scenario: A template source composes after extensions

- **GIVEN** a fixed built-in source, a skill source, an extension source and a template source
- **WHEN** the registry composes
- **THEN** the template entries come last, carry `template:<name>` ids, and appear under `templates` in `/help`

#### Scenario: A template cannot shadow a built-in

- **GIVEN** a template source offering `help` while `/help` is a built-in
- **WHEN** the registry composes
- **THEN** the template entry is dropped, the diagnostic names `builtin:help` and `template:help`, and the built-in still dispatches

## MODIFIED Requirements

### Requirement: Command registry

The CLI SHALL compose one command registry from ordered command sources. A source SHALL expose a `SourceId`, `DescribeAsync(CancellationToken)` returning its `CommandDescriptor` list, and `InvokeAsync(CommandInvocation, CancellationToken)` returning a `CommandResult`. A descriptor SHALL carry a bare name (no slash), a description, an optional argument hint, the source id and a kind of `Builtin`, `Skill`, `Template` or `Extension`. Invocation SHALL carry the bare name and the argument remainder. The registry SHALL order entries by kind — built-ins, then skills, then extensions, then templates — then by source registration order, then by descriptor order, and SHALL keep the first entry for a duplicate name while logging one diagnostic naming both sources. The registry SHALL be the only input to `/` autocomplete, dispatch and `/help`. Built-in commands SHALL be the first source and SHALL keep their names, `/help` grouping, descriptions, busy-session refusals and notices. A skill SHALL be described as `skill:<name>` and invoked through the existing skill path, so `/skill:<name>` keeps its today's wrapping and notices. The registry SHALL invoke handlers asynchronously with the caller's cancellation token; an `OperationCanceledException` for a cancelled token SHALL propagate, while any other handler exception SHALL become an error notice and a log entry naming the command and source, never a crash. A source that throws while describing SHALL be logged and skipped for that composition. The new public contract SHALL move the extension API version as a semver-minor addition. `/help` SHALL list every runnable command in the registry, not only built-ins: the existing built-in groups first and unchanged, then one section per non-built-in kind that has commands (`skills`, `extensions`, `templates`) in the registry's kind order, each section sorted by the descriptor's name and omitted when it has no rows, with rows carrying the typed name and the description and the `/skill:<name>` placeholder staying in its built-in group; a session with no non-built-in commands SHALL print the built-in listing exactly as before. Every entry SHALL carry one source-qualified id — `builtin:<name>`, `skill:<name>`, `template:<name>` or `ext:<extension id>:<name>` — computed by the host from the descriptor's kind, source and name, and used by the duplicate diagnostic, every log line and every failure notice beside the source id, so that two sources offering one name stay distinguishable; the id SHALL NOT change the typed command text or an autocomplete row, and a source SHALL NOT be able to set it. A handler failure SHALL be reported as a teaching error notice naming the command's id, its source, the reason and one recovery step (`/extensions` for an extension command, `/skills` for a skill), SHALL carry no stack trace, and SHALL leave the session running.

#### Scenario: Built-ins are first and unchanged

- **GIVEN** a composed registry
- **WHEN** the built-in descriptors are compared with the built-in table
- **THEN** they appear first, in table order, with the same names, descriptions and `/help` groups as before

#### Scenario: Source order is deterministic and first source wins

- **GIVEN** sources contributing commands of different kinds and a duplicate name across two sources
- **WHEN** the registry composes
- **THEN** descriptors are ordered built-ins, skills, extensions, templates, the duplicate appears once, and the first source after that order owns it

#### Scenario: Autocomplete, dispatch and help all read the registry

- **GIVEN** a source contributing a command that is not a built-in
- **WHEN** the command is completed, submitted and listed by `/help`
- **THEN** completion and dispatch use the registry's entry, and `/help` reflects the registry's built-in entries

#### Scenario: A throwing handler becomes a notice and a log entry

- **GIVEN** a registered command whose handler throws
- **WHEN** the command is invoked
- **THEN** the session receives an error result, renders an error notice, logs the command and source, and the session continues

#### Scenario: Cancellation reaches the handler

- **GIVEN** a registered command handler awaiting the invocation token
- **WHEN** the caller cancels
- **THEN** the handler observes cancellation and the cancellation propagates

#### Scenario: The skill invocation keeps its behaviour

- **GIVEN** a valid trusted skill and an unknown skill name
- **WHEN** `/skill:<name>` is submitted for each
- **THEN** the valid one wraps its body through the normal prompt path and the unknown one keeps the existing unknown-skill notice

#### Scenario: Help lists every source's commands

- **GIVEN** a composed registry holding a built-in, a skill and an extension command
- **WHEN** `/help` is submitted
- **THEN** the built-in groups print first and unchanged, then a `skills` section and an `extensions` section listing their commands

#### Scenario: An empty source adds no help section

- **GIVEN** a composed session with no skills and no extension commands
- **WHEN** `/help` is submitted
- **THEN** the listing is the built-in groups exactly as before, with no `skills`, `extensions` or `templates` heading

#### Scenario: Every command has a source-qualified id

- **GIVEN** a built-in `/help` and two extensions `alpha` and `beta` whose sources both provide `ping`
- **WHEN** the registry composes
- **THEN** the ids read `builtin:help`, `ext:alpha:ping` and `ext:beta:ping`, they are distinct, and the duplicate diagnostic names both sources and both `ping` ids

#### Scenario: A failing handler names the command and the recovery step

- **GIVEN** a loaded extension command whose handler throws
- **WHEN** it is submitted
- **THEN** the error notice names the command's id and source, the reason and `/extensions`, prints no stack trace, and the session continues

### Requirement: Extension commands

The extension contract SHALL let an extension register command sources through `IExtensionContext.RegisterCommandSource` at creation time only, with no processes, sockets or timers started by registration. A manifest `commands` entry SHALL be a declaration of an extension command; a command SHALL be runnable only when a registered source provides it and the manifest declares it. A provided command that the manifest does not declare SHALL refuse the load. A declared command that no registered source provides SHALL be logged and shown by `/extensions` as `declared, not provided`, and SHALL NOT appear in autocomplete or dispatch. A provided command whose name equals a built-in command SHALL refuse the load with a message naming the extension and the built-in. An invalid provided name (empty, whitespace or a leading slash) SHALL refuse the load naming the field. Described commands SHALL be cached at load, re-stamped with the extension id as source and the `Extension` kind, and removed with the extension on unload. An invocation SHALL be async and cancellable; a throwing handler SHALL become an error notice plus a log entry, never a crash. Remote extensions without an in-process source SHALL have every declared command reported as declared, not provided. The new context method SHALL move the extension API version as a semver-minor addition. An extension command's id SHALL be `ext:<extension id>:<bare name>`, stamped at load beside the source id and the `Extension` kind, and SHALL be the identity every diagnostic, log line and failure notice uses, so two extensions declaring one name stay distinguishable even though the bare name still dispatches to the first entry after ordering. An invocation SHALL hold a lease on its extension for its duration: unloading an extension with a live invocation SHALL cancel that invocation's token, SHALL report it to the user as a notice naming the extension and the command id, and SHALL wait a bounded time for the handler to return before the load context unloads; a handler that ignores cancellation SHALL be logged once and SHALL NOT hold the unload open beyond that bound. An unload SHALL NOT be reported as complete while a handler it cancelled is still running.

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
