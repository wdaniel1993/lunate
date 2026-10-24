## ADDED Requirements

### Requirement: Single-file extension discovery

The host SHALL treat every `*.cs` file directly under `.lunate/extensions/` (project) or `~/.lunate/extensions/` (user) as a single-file extension candidate, discovered beside packaged extensions, and SHALL report each one by its declared id. A file that declares no extension id SHALL be listed as `invalid (no #:extension)` and SHALL NOT be compiled. A file whose declared id collides with another candidate or with a packaged extension SHALL be refused with a message naming both paths.

#### Scenario: A file with a header appears in the extension list

- **GIVEN** `~/.lunate/extensions/acme.cs` whose first lines declare `#:extension acme.tools@1.0.0`
- **WHEN** the session composes
- **THEN** `/extensions` lists `acme.tools` as `not-built`, and no compiler runs

#### Scenario: A file without a header is not compiled

- **GIVEN** a `.cs` file in the extensions directory with no `#:extension` directive
- **WHEN** the session composes
- **THEN** it is listed as `invalid (no #:extension)` and nothing is compiled or restored

### Requirement: Header directives declare what the file provides

The header of a single-file extension SHALL be its manifest: `#:extension <id>@<version>` (exactly one), `#:tool <name>` and `#:hook <name>` (repeatable) declare the registrations the file may make, and `#:package <id>@<version>` (repeatable) declares the packages the build restores. The directives SHALL be parsed by the same scanner as `#:package`, before any compilation, and their bytes SHALL be part of the trusted source digest. A registration the header does not declare SHALL refuse the load with a message naming the registration and the missing directive. A declared registration the file never makes SHALL be warned and shown by `/extensions` as `declared, not provided`.

#### Scenario: An undeclared tool registration is refused

- **GIVEN** a single-file extension whose header declares `#:tool fetch_issue` only
- **WHEN** the file also registers a tool named `close_issue`
- **THEN** the load is refused, naming `close_issue` and the missing `#:tool close_issue`

#### Scenario: A declared tool that is never registered is a warning

- **GIVEN** a header declaring `#:tool fetch_issue` and a body that registers nothing
- **WHEN** the extension loads
- **THEN** the extension is loaded, and `/extensions` shows `fetch_issue` as `declared, not provided`

### Requirement: Directive grammar

The host SHALL parse directives with its own scanner and SHALL NOT call SDK code to do it. `#:package` SHALL accept exactly the documented .NET 10 file-based-app syntax — `#:package <id>@<version>`, where the version is a NuGet version or version range — and `#:extension` SHALL accept one id and one exact version. `#:sdk`, `#:property` and `#:project` SHALL be refused with a message naming the directive. An unrecognised `#:` line in the header SHALL be ignored with a warning rather than fail the file. The accepted grammar SHALL be pinned by tests taken from the documented .NET 10 syntax, so an upstream divergence fails a test instead of changing behaviour silently.

#### Scenario: A version range is accepted

- **GIVEN** a header line `#:package Octokit@[14.0.0,15.0.0)`
- **WHEN** the header is parsed
- **THEN** the package is accepted with that range

#### Scenario: A refused directive is named

- **GIVEN** a header line `#:property LangVersion=preview`
- **WHEN** the header is parsed
- **THEN** the extension is refused with a message naming `#:property`

### Requirement: Trust before restore

A single-file extension SHALL be trusted before it is restored or compiled, and the trust decision SHALL be bound to a digest over `sha256("lunate-extension-v1" | id | each declared package id@version | source bytes including the header | lock bytes)`. The prompt SHALL name the file, the declared packages, and that analyzers and source generators in those packages run at build time. When the digest differs from the trusted one, the host SHALL stop before any feed request, mark the extension `stale`, and ask again. A trust decision SHALL be per file, stored under `~/.lunate/trust.json`, and SHALL NOT be inherited from another file or another path.

#### Scenario: A stale digest makes no feed request

- **GIVEN** a trusted, built single-file extension
- **WHEN** one byte of its source changes and the session composes with no network available
- **THEN** the extension is `stale`, the user is asked again, and the test observes zero HTTP requests to any feed

#### Scenario: The prompt says what will run

- **GIVEN** a file declaring `#:package Octokit@14.0.0`
- **WHEN** the trust prompt is shown
- **THEN** it names the file, `Octokit@14.0.0`, and that build-time analyzers and generators from the package will run

### Requirement: Restore, lock and compiled cache

A restore SHALL run in process through the NuGet client libraries, which SHALL be loaded only when a restore runs and SHALL NOT be loaded before the first restore in a session. Resolved packages SHALL go to NuGet's global packages folder, respecting `NUGET_PACKAGES`, and SHALL NOT be copied into a Lunate-owned folder. Each extension SHALL keep only its compiled assembly, its `extension.lock.json` (exact versions, SHA-512 per package, resolved asset list) and a pointer to the current build under `~/.lunate/cache/extensions/<id>/`, keyed by `sha256(sourceHash | lockHash | apiVersion | TFM | RID)[..16]`. A successful compile SHALL keep the last two builds and delete older ones; uninstalling or removing the extension SHALL delete its builds; `lunate cache prune` SHALL remove entries no extension points at. The lock file SHALL NOT be updated implicitly — a restore that would change it SHALL report the change and ask.

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
