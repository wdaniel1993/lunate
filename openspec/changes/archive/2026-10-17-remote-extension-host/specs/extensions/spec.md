# extensions — delta for remote-extension-host

## MODIFIED Requirements

### Requirement: Extension manifest
An extension SHALL declare an `extension.json` manifest with `id` (lowercase, `[a-z0-9-]+`), `version`, `apiVersion` (a range), exactly one of `entryAssembly` (an in-process extension) or `runtime` (an out-of-process extension) and optional `tools`, `commands`, `hooks`, `services`, `settingsSchema` and `capabilities`. `runtime` SHALL mirror the MCP server registration shape — `command`, `args`, `workingDirectory`, `env` (values to set), `envPassthrough` (names inherited from the parent), `callTimeout` — and a manifest with both `entryAssembly` and `runtime`, or with neither, SHALL be refused naming the file and the conflict. Unknown top-level fields SHALL be ignored; malformed known fields SHALL fail with an error naming the file and the field; duplicate declarations SHALL be rejected. The API-version range grammar SHALL be exact `x.y.z` or caret `^x.y.z`; other forms SHALL be refused as unsupported. An incompatible range SHALL be refused with a message naming the extension, its range and the current API version.

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

#### Scenario: A remote manifest parses

- **GIVEN** a manifest with `runtime` instead of `entryAssembly`
- **WHEN** it is parsed
- **THEN** the parsed model carries the runtime command, args, working directory, env, envPassthrough and call timeout

#### Scenario: A manifest must declare exactly one runtime

- **GIVEN** a manifest with both `entryAssembly` and `runtime`
- **WHEN** it is validated
- **THEN** the refusal names the file and the conflict

## ADDED Requirements

### Requirement: Out-of-process extension host

An extension whose manifest declares `runtime` SHALL run as a child process instead of an assembly load: discovery, scope and failed-extension containment SHALL be identical to in-process extensions, except that trust SHALL hash the manifest together with the extension directory contents (entry script and bundled files) — swapping a bundled file after trust SHALL re-prompt — and the trust prompt SHALL state that the extension runs with the user's privileges. The host SHALL build hook and service proxies from the manifest's declarations without starting anything, and SHALL start the process lazily on the extension's first use — never during discovery, load, registration or composition — through the core process capability; a manifest startup mode `background` SHALL instead start it after the interactive UI is ready, never on the startup path, and a subscribed `session-started` SHALL be delivered when the session is live (the same post-ready point) or on the child's first successful start, whichever comes first, so no child ever starts on the startup path. A relative `command` or argument path SHALL resolve against the extension directory and the default working directory SHALL be the extension directory. The child's environment SHALL be exactly the minimal base set (PATH, HOME/USERPROFILE, SYSTEMROOT, TEMP/TMP/TMPDIR, LANG) plus `envPassthrough` plus `env`, with no inheritance — Lunate's provider keys SHALL NOT leak — built by the shared scrubber (the code-mode sample's implementation, promoted into the contract assembly; one implementation). The wire SHALL be newline-delimited JSON-RPC 2.0: UTF-8, no BOM, no batches; stdout SHALL be protocol-only — a non-JSON line is a protocol error and a line over 16 MB is a protocol error, never an OOM — and stderr SHALL be captured into the extension log, capped. The first call SHALL be an `initialize` request carrying `protocolVersion` and capabilities, the manifest echo, the workspace info and the declared hooks and services; a version mismatch SHALL refuse the extension with a clear status — never best effort — and the result SHALL match the manifest: a hook or service the child implements but the manifest does not declare SHALL be refused, a declaration the child does not implement SHALL be warned (the in-process rules, unchanged). The channel SHALL be bidirectional and reentrant — the child may call host services while handling a hook — with separate id spaces per direction and concurrent requests allowed. Cancellation SHALL map `$/cancelRequest` notifications to `CancellationToken` in both directions. The host SHALL forward only the hooks the manifest subscribes to: observer hooks (`SessionStarted`, `SessionEnding`, `ProviderStreamEvent`, `RunSettled`, `ModelChanged`, `ToolsChanged`, `FileChanged`) SHALL be notifications — fire-and-forget, and `ProviderStreamEvent` deltas SHALL not be forwarded unless explicitly subscribed — while interceptor hooks (`ProjectTrust`, `InputReceived`, `RunStarting`, `ContextBuilding`, `MessageCompleted`, `ToolCalling`, `ToolResultReady`, `TurnEnded`, `Compacting`) SHALL be requests. Timeouts and failures SHALL follow the catalogue: observers drop with a warning; interceptors take the hook's no-opinion semantics (keep original / skip) with a warning, except the gating hooks — `ToolCalling` blocks fail-safe and `ProjectTrust` denies. Every call SHALL carry the run's ids (RunId, and ParentRunId when present) and the workspace; one child SHALL serve the whole session, subagent and worktree runs included. Service calls SHALL cross both ways — consumption by name through the registry, and calls on services the remote extension registered — as JSON-RPC requests with JSON arguments and results, and services a remote extension registers SHALL be async-only (in-process consumers SHALL NOT call them on a synchronous hot path). A process that exits SHALL fail in-flight calls immediately, without waiting for their timeouts, with one notice, flip the extension to a failed status with the reason `the extension process exited`, and the session SHALL continue; the host SHALL attempt at most one automatic restart per session, after which the extension reads failed until a manual restart from `/extensions`. At session end the host SHALL send the shutdown request, allow a bounded grace, then kill the process tree — a Job Object on Windows, the children-first POSIX walk on Unix. The process boundary SHALL provide runtime isolation — dependencies, language, crash containment — and SHALL NOT be described or treated as a permission boundary.

#### Scenario: A remote extension starts lazily and answers a hook

- **GIVEN** an installed remote extension whose manifest declares a hook
- **WHEN** the hook first fires in a session
- **THEN** the process starts, the handshake succeeds and the hook's result crosses back and takes effect

#### Scenario: A protocol version mismatch refuses

- **GIVEN** a remote extension whose `initialize` result reports a different protocol version
- **WHEN** the extension first runs
- **THEN** it is refused with a clear status, never driven best-effort, and the session continues

#### Scenario: A hook handler calls back into the host

- **GIVEN** a started remote extension handling a hook
- **WHEN** its handler calls a host service before answering
- **THEN** the service call is answered while the hook call is in flight, and the hook's result still lands

#### Scenario: Declarations and the initialize result must agree

- **GIVEN** a remote extension whose initialize result implements a hook its manifest does not declare
- **WHEN** the handshake completes
- **THEN** the extension is refused; a declared-but-unimplemented hook only warns

#### Scenario: A timed-out gating hook fails closed

- **GIVEN** a remote `ToolCalling` handler that does not answer within its timeout
- **WHEN** the tool call dispatches
- **THEN** the call is blocked with a reason and the failure is reported

#### Scenario: A crash fails fast and restarts at most once

- **GIVEN** a started remote extension with a hook call in flight
- **WHEN** its process exits
- **THEN** the in-flight call fails immediately with one notice, at most one automatic restart is attempted, and after that the extension reads failed until a manual restart from `/extensions`

#### Scenario: The child environment is exactly base plus passthrough plus env

- **GIVEN** a remote extension whose manifest declares one environment variable and one passthrough name, and a parent process holding a provider key
- **WHEN** the child starts
- **THEN** the declared variable and passthrough name are present, the provider key is absent, and nothing else is inherited

#### Scenario: Session end stops the process within the grace

- **GIVEN** a started remote extension
- **WHEN** the session ends
- **THEN** the shutdown request is sent, the process exits within the grace window, and a process that ignores it is killed children-first

#### Scenario: A non-JSON stdout line is a protocol error

- **GIVEN** a remote extension that writes a non-JSON line to stdout
- **WHEN** the host reads it
- **THEN** it is treated as a protocol error, never as protocol data

#### Scenario: A background extension starts after the UI is ready

- **GIVEN** a remote extension whose manifest declares the `background` startup mode
- **WHEN** an interactive session starts
- **THEN** the process starts only after the UI is ready, and the startup path and its budget are untouched

#### Scenario: Trust covers the extension directory

- **GIVEN** a trusted remote extension whose bundled entry script changes
- **WHEN** the next session starts
- **THEN** the trust prompt fires again

### Requirement: Remote wire conformance

Every hook payload and result DTO and the service-call envelope SHALL round-trip over the actual wire — serialized, framed as one JSON-RPC message, parsed and compared by the peer — with the inventory covering the hook catalogue exhaustively so a new DTO cannot ship without a wire round-trip. A JSON Schema generated from the hook and service DTOs SHALL be published with the docs and shipped in `Lunate.Extensibility.Testing`, so non-C# authors do not reverse-engineer C#. `Lunate.Extensibility.Testing` SHALL provide a conformance runner that can be pointed at any remote extension executable. The node reference extension SHALL live in the fitness suite (`samples/extensions`, its tests running in CI like every sample), and a production composition test SHALL load a remote extension through the real host — hook delivered, service call answered.

#### Scenario: A new DTO without a wire round-trip fails conformance

- **GIVEN** a hook payload DTO added to the catalogue without a wire round-trip entry
- **WHEN** the conformance suite runs
- **THEN** it fails naming the DTO

#### Scenario: The conformance runner checks an arbitrary executable

- **GIVEN** a remote extension executable that speaks the protocol
- **WHEN** the conformance runner is pointed at it
- **THEN** it performs start, handshake, a hook round-trip, a service call in each direction and shutdown, and reports the outcome

#### Scenario: The reference extension is wired through the real host

- **GIVEN** the node reference extension installed in a test workspace
- **WHEN** a composed session runs a tool call
- **THEN** its hook observes the call, its registered service answers a host call, and its service consumption receives a result
