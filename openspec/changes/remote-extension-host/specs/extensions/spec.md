# extensions — delta for remote-extension-host

## MODIFIED Requirements

### Requirement: Extension manifest
An extension SHALL declare an `extension.json` manifest with `id` (lowercase, `[a-z0-9-]+`), `version`, `apiVersion` (a range), exactly one of `entryAssembly` (an in-process extension) or `runtime` (an out-of-process extension) and optional `tools`, `commands`, `hooks`, `services`, `settingsSchema` and `capabilities`. `runtime` SHALL mirror the MCP server registration shape — `command`, `args`, `workingDirectory`, `environment`, `callTimeout` — and a manifest with both `entryAssembly` and `runtime`, or with neither, SHALL be refused naming the file and the conflict. Unknown top-level fields SHALL be ignored; malformed known fields SHALL fail with an error naming the file and the field; duplicate declarations SHALL be rejected. The API-version range grammar SHALL be exact `x.y.z` or caret `^x.y.z`; other forms SHALL be refused as unsupported. An incompatible range SHALL be refused with a message naming the extension, its range and the current API version.

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
- **THEN** the parsed model carries the runtime command, args, working directory, environment and call timeout

#### Scenario: A manifest must declare exactly one runtime

- **GIVEN** a manifest with both `entryAssembly` and `runtime`
- **WHEN** it is validated
- **THEN** the refusal names the file and the conflict

## ADDED Requirements

### Requirement: Out-of-process extension host

An extension whose manifest declares `runtime` SHALL run as a child process instead of an assembly load: discovery, scope, trust (per-repository content hash; global trusted by installation) and failed-extension containment SHALL be identical to in-process extensions. The host SHALL start the process lazily on the extension's first use — never during discovery or registration — through the core process capability, with the child's environment exactly the manifest's declared allow-list (no inheritance; Lunate's provider keys SHALL NOT leak) and stdout reserved for the protocol (stderr SHALL be captured into the extension log with a source prefix). The first call SHALL be an `initialize` handshake: protocol version (v1, compared for equality), the manifest echo, the workspace info and the declared hooks and services; a mismatch SHALL fail the extension with a logged reason while the session continues. Hook dispatches SHALL cross as one JSON-RPC call per dispatch with the catalogue payload and result DTOs, and the declared hook semantics, ordering and failure policy SHALL apply unchanged — a JSON-RPC error, a timeout or a dead process SHALL map to the hook's failure policy exactly like an in-process handler throwing, never a crashed session. Service calls SHALL cross both ways (consumption by name through the registry, and calls on services the remote extension registered) as JSON-RPC calls with JSON arguments and results. A process that exits SHALL flip the extension to a failed status with the reason `the extension process exited`; its hooks SHALL be skipped with a logged reason and the session SHALL continue. At session end the host SHALL send the shutdown request, allow a bounded grace, then kill the process tree children-first (never the framework's tree kill on POSIX). The process boundary SHALL provide runtime isolation — dependencies, language, crash containment — and SHALL NOT be described or treated as a permission boundary; in-process trust rules are unchanged.

#### Scenario: A remote extension starts lazily and answers a hook

- **GIVEN** an installed remote extension whose manifest declares a hook
- **WHEN** the hook first fires in a session
- **THEN** the process starts, the handshake succeeds and the hook's result crosses back and takes effect

#### Scenario: A crashed remote extension is contained

- **GIVEN** a started remote extension whose process exits mid-session
- **WHEN** its next hook would fire
- **THEN** the extension reads failed with `the extension process exited`, the skip is logged and the session continues

#### Scenario: The child environment is exactly the declared allow-list

- **GIVEN** a remote extension whose manifest declares one environment variable and a parent process holding a provider key
- **WHEN** the child starts
- **THEN** the declared variable is present and the provider key is absent

#### Scenario: Session end stops the process within the grace

- **GIVEN** a started remote extension
- **WHEN** the session ends
- **THEN** the shutdown request is sent, the process exits within the grace window, and a process that ignores it is killed children-first

### Requirement: Remote wire conformance

Every hook payload and result DTO and the service-call envelope SHALL round-trip over the actual wire — serialized, framed as one JSON-RPC message, parsed and compared by the peer — and a scripted out-of-process reference extension SHALL exercise the host end to end in CI (start, handshake, a hook call, a service call in each direction, shutdown). The conformance inventory SHALL cover the hook catalogue exhaustively so a new DTO cannot ship without a wire round-trip.

#### Scenario: A new DTO without a wire round-trip fails conformance

- **GIVEN** a hook payload DTO added to the catalogue without a wire round-trip entry
- **WHEN** the conformance suite runs
- **THEN** it fails naming the DTO

#### Scenario: The scripted reference extension exercises the host

- **GIVEN** the scripted remote reference extension installed in a test workspace
- **WHEN** a composed session runs a tool call
- **THEN** its hook observes the call, its registered service answers a host call, and its service consumption receives a result
