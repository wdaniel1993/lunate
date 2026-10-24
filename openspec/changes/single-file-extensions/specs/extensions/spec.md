## ADDED Requirements

### Requirement: Single-file extension discovery

The host SHALL treat every `*.cs` file directly under `.lunate/extensions/` (project) or `~/.lunate/extensions/` (user) as a single-file extension candidate, discovered beside packaged extensions, and SHALL report each one by its declared id. A file that declares no extension id SHALL be listed as `invalid (no #:extension)` and SHALL NOT be compiled. A file whose declared id collides with another candidate or with a packaged extension SHALL be refused with a message naming both paths.

#### Scenario: A file with a header appears in the extension list

- **GIVEN** `~/.lunate/extensions/acme.cs` whose first lines declare `#:extension acme-tools@1.0.0`
- **WHEN** the session composes
- **THEN** `/extensions` lists `acme-tools` as `not-built`, and no compiler runs

#### Scenario: A file without a header is not compiled

- **GIVEN** a `.cs` file in the extensions directory with no `#:extension` directive
- **WHEN** the session composes
- **THEN** it is listed as `invalid (no #:extension)` and nothing is compiled or restored

### Requirement: Header directives declare what the file provides

The header SHALL mirror the packaged manifest's declaration fields one to one — `#:tool`, `#:command`, `#:hook`, `#:service`, `#:capability` and `#:provider` for the manifest's `tools`, `commands`, `hooks`, `services`, `capabilities` and `modelProviders` — so a single file can never be unable to register something a package can. The header is the only declaration source for a single file; there is no sidecar manifest in this version. Directives SHALL be parsed before any compilation and their bytes SHALL be part of the trusted source digest. A registration the header does not declare SHALL refuse the load, naming the registration and the missing directive. A declared registration the file never makes SHALL be warned and shown by `/extensions` as `declared, not provided`.

#### Scenario: An undeclared tool registration is refused

- **GIVEN** a single-file extension whose header declares `#:tool fetch_issue` only
- **WHEN** the file also registers a tool named `close_issue`
- **THEN** the load is refused, naming `close_issue` and the missing `#:tool close_issue`

#### Scenario: Every manifest declaration kind has a directive

- **GIVEN** the packaged manifest's set of declaration fields
- **WHEN** the header grammar is inspected
- **THEN** each field maps to a directive, and a test fails if a manifest field is added without one

#### Scenario: A declared tool that is never registered is a warning

- **GIVEN** a header declaring `#:tool fetch_issue` and a body that registers nothing
- **WHEN** the extension loads
- **THEN** the extension is loaded, and `/extensions` shows `fetch_issue` as `declared, not provided`

### Requirement: Directive grammar and position

The host SHALL parse directives with its own scanner and SHALL NOT call SDK code to do it. The extension id SHALL follow the packaged manifest's rule exactly (`[a-z0-9-]+`), so a single file can be promoted to a package without renaming itself. A `#:tool` name SHALL match `[A-Za-z0-9_-]`, and the model-facing name `<id>__<tool>` SHALL fit the provider's 64-character limit; a pair that cannot fit SHALL be refused at discovery, naming the composed name and both halves. `#:package` SHALL accept exactly the documented .NET 10 file-based-app syntax — `#:package <id>@<version>`, where the version is a NuGet version or version range — and `#:extension` SHALL accept one id and one exact version. Only the leading block SHALL be scanned: directives, comments and blank lines before the first C# token. A `#:` line after code begins SHALL be an error naming its line number, never a silently ignored directive. `#:sdk`, `#:property` and `#:project` SHALL be refused with a message naming the directive. An unrecognised `#:` line inside the leading block SHALL be ignored with a warning. The accepted grammar SHALL be pinned by tests taken from the documented .NET 10 syntax.

#### Scenario: A version range is accepted

- **GIVEN** a header line `#:package Octokit@[14.0.0,15.0.0)`
- **WHEN** the header is parsed
- **THEN** the package is accepted with that range

#### Scenario: A directive after code is an error, not a silent no-op

- **GIVEN** a file whose line 12 is `#:tool late` and whose line 3 already contains a class declaration
- **WHEN** the header is parsed
- **THEN** the extension is refused with a message naming line 12

#### Scenario: A dotted id is refused like a package's would be

- **GIVEN** a header line `#:extension acme.tools@1.0.0`
- **WHEN** the header is parsed
- **THEN** the extension is refused, because a package would refuse the same id and a tool name may not contain a dot

#### Scenario: A tool name that cannot fit the provider limit is refused with both halves

- **GIVEN** a 40-character extension id and a 23-character tool name, whose composed name is 65 characters
- **WHEN** the header is parsed
- **THEN** the extension is refused, naming the composed name, the id and the tool name

#### Scenario: A refused directive is named

- **GIVEN** a header line `#:property LangVersion=preview`
- **WHEN** the header is parsed
- **THEN** the extension is refused with a message naming `#:property`

### Requirement: Trust before restore

A single-file extension SHALL be trusted before it is restored or compiled. The **first** trust SHALL cover the id, the declared packages as written (including version ranges) and the source bytes including the header; no lock exists yet. The first successful restore SHALL write the lock file and bind its hash into the trust record automatically — a restore downloads but executes nothing, so this needs no second prompt, provided compilation only ever runs against the bound lock. From then on the digest SHALL include the lock bytes, and any change to the lock SHALL make the extension `stale` and require a new decision. When the digest differs from the trusted one, the host SHALL stop before any feed request and ask again. A trust decision SHALL be per file, stored under `~/.lunate/trust.json`, and SHALL NOT be inherited from another file or path. The prompt SHALL name the file and its declared packages "and their dependencies", because the transitive set is not known before restore; after a restore `/extensions` SHALL show the resolved graph from the lock.

#### Scenario: First trust, restore, lock bound, compile

- **GIVEN** a new single-file extension declaring `#:package Octokit@14.0.0`
- **WHEN** the user trusts it and the first restore succeeds
- **THEN** the lock is written, its hash is bound into the trust record without a second prompt, and the compile runs against that lock

#### Scenario: A lock edited by hand is stale and makes no feed request

- **GIVEN** a trusted, built extension whose `extension.lock.json` is edited by hand
- **WHEN** the session composes with no network available
- **THEN** the extension is `stale`, the user is asked again, and the test observes zero HTTP requests to any feed

#### Scenario: A stale digest from a source edit makes no feed request

- **GIVEN** a trusted, built single-file extension
- **WHEN** one byte of its source changes and the session composes with no network available
- **THEN** the extension is `stale`, the user is asked again, and the test observes zero HTTP requests to any feed

### Requirement: Build-time code execution is opt-in

Compilation SHALL reference only the packages' compile-time assemblies; their analyzers and source generators SHALL NOT be loaded by default. A `#:generators on` header directive SHALL opt the file in, SHALL be part of the trusted digest, and only then SHALL the trust prompt state that build-time code from the named packages will run. `/extensions` SHALL show, for a package that ships generators while the file did not opt in, `generators skipped: <package>`, so the omission is visible rather than silent.

#### Scenario: Generators are skipped without the directive

- **GIVEN** a file declaring `#:package Refit@8.0.0`, a package that ships a source generator
- **WHEN** it is compiled without `#:generators on`
- **THEN** no generator runs, the compile succeeds on the reference assemblies alone, and `/extensions` shows `generators skipped: Refit`

#### Scenario: The opt-in changes the prompt

- **GIVEN** the same file with `#:generators on`
- **WHEN** the trust prompt is shown
- **THEN** it states that build-time code from the declared packages will run, and toggling the directive makes the digest stale

### Requirement: Reference assemblies for compilation

Compilation SHALL use the .NET reference assemblies matching the running runtime, and the host SHALL NOT require an installed SDK to obtain them. The reference pack (`Microsoft.NETCore.App.Ref`, the runtime's version and RID) SHALL be restored through the same in-process NuGet engine into the global packages folder on first compile and reused from there. A published self-contained single-file binary SHALL compile a single-file extension with an empty `HOME` and no `dotnet` on `PATH`; that run SHALL be a test.

#### Scenario: A self-contained binary compiles with no SDK and an empty home

- **GIVEN** the published `osx-arm64` single-file binary, `HOME` pointing at an empty directory, and no `dotnet` on `PATH`
- **WHEN** a single-file extension declaring one package is trusted and compiled
- **THEN** the reference pack is restored into the global packages folder, the compile succeeds, and a second run reuses the cache without a feed request

### Requirement: Restore, lock and compiled cache

A restore SHALL run in process through the NuGet client libraries, which SHALL be loaded only when a restore runs. Resolved packages SHALL go to NuGet's global packages folder, respecting `NUGET_PACKAGES`, and SHALL NOT be copied into a Lunate-owned folder. Each extension SHALL keep only its compiled assembly, its `extension.lock.json` (exact versions, SHA-512 per package, resolved asset list) and a pointer to the current build under `~/.lunate/cache/extensions/<id>/`, keyed by `sha256(sourceHash | lockHash | apiVersion | TFM | RID)[..16]`. A successful compile SHALL keep the last two builds and delete older ones; removing the extension SHALL delete its builds; `lunate cache prune` SHALL remove entries no extension points at. The lock SHALL NOT be updated implicitly — a restore that would change it SHALL report and ask.

#### Scenario: A second compile leaves two builds

- **GIVEN** an extension built once, then edited and built again, then edited and built a third time
- **WHEN** the third build succeeds
- **THEN** two build directories remain under its cache entry and the oldest is gone

#### Scenario: No NuGet assembly before a restore

- **GIVEN** a session whose extensions are all already built
- **WHEN** the session starts
- **THEN** no `NuGet.*` assembly is loaded into the process

### Requirement: Host assemblies win over the extension's copies

A single-file extension SHALL load into its own collectible context, and `Lunate.Extensibility.Abstractions`, `Lunate.Agent`, `Lunate.Ai`, `Microsoft.Extensions.AI*` and `System.Text.Json` SHALL always resolve to the host's copies, including when a package ships its own. Compatibility SHALL be decided by the Abstractions version the extension references: a reference above the host's `ExtensionApi` version SHALL refuse the load with `requires Lunate ≥ x.y`. When a copy was ignored, `/extensions` SHALL show a one-line warning naming the assembly, and the authoring template and docs SHALL show the reference as `PrivateAssets="all"` with `ExcludeAssets="runtime"`.

#### Scenario: A copied Abstractions is ignored, not loaded twice

- **GIVEN** a package that ships `Lunate.Extensibility.Abstractions.dll`
- **WHEN** the extension loads
- **THEN** the host's copy is used, `/extensions` warns once, and the load succeeds

#### Scenario: A newer contract is refused by version

- **GIVEN** an extension referencing Abstractions 2.0 while the host implements 1.7
- **WHEN** it loads
- **THEN** the load is refused with `requires Lunate ≥ 2.0`

### Requirement: Extension states and reload

`/extensions` SHALL show each single-file extension in one of `not-built`, `restoring`, `built`, `stale`, `failed (trust|restore|compile)` or `invalid (no #:extension)`, with the reason on the same line for a failure. `/extensions reload <id>` SHALL preflight trust, build before touching the running extension, then cancel its live invocations, show a notice, drain for a bounded time, dispose and unload the context, load the new build and raise `ToolsChanged`; a running turn SHALL NOT be interrupted mid-call, and a failed reload SHALL leave the previously loaded build in place.

#### Scenario: A reload that fails to compile keeps the old build

- **GIVEN** a loaded extension that is edited into a compile error
- **WHEN** `/extensions reload <id>` runs
- **THEN** the extension stays loaded at its previous build and `/extensions` reports `failed (compile)` with the first error

#### Scenario: A live invocation is drained, not cut

- **GIVEN** an extension tool call in flight
- **WHEN** `/extensions reload <id>` runs
- **THEN** the call finishes or is cancelled with a notice, the context is disposed within the bounded drain, and only then does the new build load

### Requirement: The model never approves trust

Trust SHALL be a user decision only. The model MAY write or edit a single-file extension and MAY ask for a reload, but no tool, command or protocol path SHALL let it record a trust decision, suppress a prompt, or load a changed file whose digest is stale. A model-initiated reload of a changed file SHALL end at the trust prompt and wait for the user.

#### Scenario: A model-initiated reload stops at the prompt

- **GIVEN** a loaded extension the model has just edited
- **WHEN** the model requests a reload through its tool
- **THEN** the reload stops at the trust prompt, the changed file is not compiled, and the old build stays loaded until the user answers
