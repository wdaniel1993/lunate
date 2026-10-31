## MODIFIED Requirements

### Requirement: Extension manifest

An extension SHALL declare an `extension.json` manifest with `id` (lowercase, `[a-z0-9-]+`),
`version`, `apiVersion` (a range), exactly one of `entryAssembly` (an in-process extension) or
`runtime` (an out-of-process extension) and optional `tools`, `commands`, `hooks`, `services`,
`writeVerifiers`, `settingsSchema` and `capabilities`. Each `writeVerifiers` entry SHALL declare a
unique `name` and one or more filename patterns. Patterns SHALL be non-empty filename globs without
path separators and SHALL match normalized basenames case-insensitively. `runtime` SHALL mirror the
MCP server registration shape — `command`, `args`, `workingDirectory`, `env` (values to set),
`envPassthrough` (names inherited from the parent), `callTimeout` — and a manifest with both
`entryAssembly` and `runtime`, or with neither, SHALL be refused naming the file and the conflict.
Unknown top-level fields SHALL be ignored; malformed known fields SHALL fail with an error naming
the file and the field; duplicate declarations SHALL be rejected. The API-version range grammar
SHALL be exact `x.y.z` or caret `^x.y.z`; other forms SHALL be refused as unsupported. An
incompatible range SHALL be refused with a message naming the extension, its range and the current
API version.

#### Scenario: A valid manifest parses

- **GIVEN** a manifest with all required fields and a `writeVerifiers` entry with `name` and
  `patterns`
- **WHEN** it is parsed
- **THEN** the parsed model carries every declared field and defaults optional arrays to empty

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
- **THEN** the parsed model carries the runtime command, args, working directory, env, envPassthrough
  and call timeout

#### Scenario: A manifest must declare exactly one runtime

- **GIVEN** a manifest with both `entryAssembly` and `runtime`
- **WHEN** it is validated
- **THEN** the refusal names the file and the conflict

#### Scenario: Invalid verifier patterns name the field

- **GIVEN** a manifest with an empty pattern, a path separator, or a duplicate verifier name
- **WHEN** it is validated
- **THEN** it is refused with the manifest path and `writeVerifiers` field in the diagnostic

## ADDED Requirements

### Requirement: Write-verifier extension contract

`Lunate.Extensibility.Abstractions` SHALL expose `IWriteVerifier` and
`IExtensionContext.RegisterWriteVerifier`. The extension SHALL register each declared verifier name
once; registering an undeclared verifier SHALL refuse extension activation, and a declared but
unregistered verifier SHALL be logged and omitted. The host SHALL provide an immutable verification
request containing the normalized relative path, baseline content and staged content, plus a
cancellation token. The verifier SHALL have no file-write API and SHALL return exactly one of
`verified`, `unverified` with a reason, or `refused` with diagnostic `StructuredContent`. It SHALL
NOT return replacement content. The host SHALL enforce its call deadline and SHALL remain the only
writer of the staged file.

#### Scenario: A declared verifier registers

- **GIVEN** an extension manifest declaring a verifier named `csharp`
- **WHEN** its factory registers `csharp`
- **THEN** the host exposes that verifier for the declared patterns

#### Scenario: An undeclared verifier registration is refused

- **GIVEN** a factory registering a verifier name absent from its manifest
- **WHEN** the extension is activated
- **THEN** activation is refused with the extension and undeclared verifier name

#### Scenario: A declared but absent verifier is omitted

- **GIVEN** a manifest declaring a verifier that the factory does not register
- **WHEN** the extension is activated
- **THEN** the host logs the missing registration and does not expose it

#### Scenario: The verifier result cannot replace file content

- **GIVEN** a verifier receives the immutable request
- **WHEN** it returns a result
- **THEN** the result contains only status, optional reason and structured diagnostics, never
  replacement content
