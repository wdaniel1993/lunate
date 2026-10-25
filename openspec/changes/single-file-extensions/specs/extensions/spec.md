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

A single-file extension SHALL be trusted before it is restored or compiled. The **first** trust SHALL cover the id, the declared packages as written (including version ranges) and the source bytes including the header; no lock exists yet. The first successful restore SHALL write the lock file and bind its **graph** into the trust record automatically — a restore downloads but executes nothing, so this needs no second prompt, provided compilation only ever runs against the bound lock. From then on the digest SHALL include the lock's graph half — the sources, and per package the exact version, SHA-512, resolved assets and dependency edges — and any change to that half SHALL make the extension `stale` and require a new decision. The lock's **toolset** half — target framework, RID, exact runtime, and the reference pack's version and nupkg hash — SHALL NOT be part of the digest: it SHALL be integrity-checked on restore, refused when it no longer matches its contents, included in the build cache key, and a change to it SHALL trigger a rebuild without a trust question. When the digest differs from the trusted one, the host SHALL stop before any feed request and ask again. A trust decision SHALL be per file, stored under `~/.lunate/trust.json`, and SHALL NOT be inherited from another file or path. The prompt SHALL name the file and its declared packages "and their dependencies", because the transitive set is not known before restore; after a restore `/extensions` SHALL show the resolved graph from the lock.

#### Scenario: First trust, restore, lock bound, compile

- **GIVEN** a new single-file extension declaring `#:package Octokit@14.0.0`
- **WHEN** the user trusts it and the first restore succeeds
- **THEN** the lock is written, its graph is bound into the trust record without a second prompt, and the compile runs against that lock

#### Scenario: A lock's graph edited by hand is stale and makes no feed request

- **GIVEN** a trusted, built extension whose `extension.lock.json` graph half is edited by hand
- **WHEN** the session composes with no network available
- **THEN** the extension is `stale`, the user is asked again, and the test observes zero HTTP requests to any feed

#### Scenario: A toolset change rebuilds without asking

- **GIVEN** a trusted, restored extension whose lock records runtime `10.0.13` and its reference pack
- **WHEN** it is restored on a host whose runtime is `10.0.14`, with the same source and the same resolution
- **THEN** the lock is rewritten for the new toolset and the resolution rebuilt, the trust digest is unchanged, and the trust prompt is never called

#### Scenario: A tampered toolset block is refused

- **GIVEN** a restored extension whose `extension.lock.json` toolset block is edited without its integrity hash being recomputed
- **WHEN** the extension is discovered and then restored
- **THEN** the lock is refused rather than used, the graph the user approved is still recognized as trusted, and the restore rewrites the block without asking

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

### Requirement: Compiling a single file

The host SHALL compile a trusted single-file extension in process with the Roslyn it already ships, against the reference pack restored for the running runtime, the host's contract assemblies and each locked package's compile assets — and against nothing else. A compile SHALL NOT re-resolve a lock, SHALL NOT reach a feed, and SHALL NOT load a package's analyzers or source generators unless the header opted in. A file whose digest is `stale` SHALL NOT be compiled, and a lock that does not answer for the trusted bytes SHALL NOT be used. Compilation SHALL be deterministic: the same source, the same lock and the same toolset SHALL produce a byte-identical assembly, so a rebuild is a cache hit rather than a new artifact. A failed compile SHALL report the **first** diagnostic — the file, the line, the column, the compiler id and the message — as `failed (compile) — <reason>`, and SHALL leave no build directory behind. The build SHALL be triggerable without a session through `lunate extensions build <id|path> [--json]`, which runs the same service `/extensions reload` uses; listing extensions SHALL NOT compile anything, and startup SHALL stay lazy.

#### Scenario: A compile error leaves nothing behind

- **GIVEN** a trusted, restored extension whose source has a compile error
- **WHEN** `lunate extensions build <id>` runs
- **THEN** it exits non-zero, the build reports `failed (compile)` with the first diagnostic in Roslyn's own form (`path(line,col): error CSxxxx: message`), and no build directory exists for it

#### Scenario: The same inputs build once

- **GIVEN** an extension built once
- **WHEN** it is built again with the same source, lock and toolset
- **THEN** the build is a cache hit, no compiler runs, and the assembly's bytes are unchanged

#### Scenario: A package's copy of a contract is not compiled against

- **GIVEN** a locked package that ships its own `Lunate.Extensibility.Abstractions.dll`
- **WHEN** the extension is compiled
- **THEN** the host's contract assembly is the one referenced, and the compile succeeds rather than failing on two assemblies with one identity

### Requirement: Restore, lock and compiled cache

A restore SHALL run in process through the NuGet client libraries, which SHALL be loaded only when a restore runs. Resolved packages SHALL go to NuGet's global packages folder, respecting `NUGET_PACKAGES`, and SHALL NOT be copied into a Lunate-owned folder. Each extension SHALL keep only its compiled assembly, its `extension.lock.json` and a pointer to the current build under `~/.lunate/cache/extensions/<id>/`. The lock SHALL have two halves: a `graph` (the sources, and per package the exact version, nupkg SHA-512, the resolved asset list and the dependency edges) and a `toolset` (target framework, RID, exact runtime, and the reference pack's id, version and nupkg hash, with an integrity hash over the block). The build SHALL be keyed by `sha256(sourceHash | lockHash | apiVersion | TFM | RID)`, where `lockHash` covers both halves, so a graph change and a toolset change each produce a new build. A successful compile SHALL keep the last two builds and delete older ones; removing the extension SHALL delete its builds; `lunate cache prune` SHALL remove entries no extension points at. The builds SHALL live under `builds/<key>/`, each with the compiled assembly and a manifest that records its key's inputs and the resolution a load needs, and the entry SHALL carry a pointer file naming the current build. A restore that would change the lock's graph SHALL report and ask; a restore that would change only its toolset SHALL rewrite the lock and rebuild without asking. The state root SHALL be `~/.lunate` unless `LUNATE_HOME` names an absolute path, in which case the lock, the trust record, the build cache and the extension files SHALL all resolve under it; a relative value SHALL be refused by name rather than silently ignored, because a process that must not touch the user's state depends on the override taking effect.

#### Scenario: A second compile leaves two builds

- **GIVEN** an extension built once, then edited and built again, then edited and built a third time
- **WHEN** the third build succeeds
- **THEN** two build directories remain under its cache entry and the oldest is gone

#### Scenario: The state root can be moved

- **GIVEN** `LUNATE_HOME` set to an absolute temporary directory
- **WHEN** a single file is trusted, restored and compiled
- **THEN** its lock, its trust record and its build are all under that directory, and nothing is written under the user's profile

#### Scenario: No NuGet assembly before a restore

- **GIVEN** a session whose extensions are all already built
- **WHEN** the session starts
- **THEN** no `NuGet.*` assembly is loaded into the process

### Requirement: The state root is guarded, visible and environment-only

The state root SHALL be read from the process environment only — no `.env` file, settings file, `AGENTS.md` or project file may supply `LUNATE_HOME` — because a repository that can choose the store can choose the directory its own trust records live in. A process that knows its workspace or repository root SHALL refuse a `LUNATE_HOME` whose store resolves inside it, before opening the store, with the message `LUNATE_HOME points inside the workspace; trust records there could be written by the repository`. Containment SHALL compare real paths (symlinks resolved, `.` and `..` resolved, trailing separators ignored, the platform's case rule), a store equal to the root SHALL count as inside, and a sibling or unrelated directory SHALL be accepted. When `LUNATE_HOME` is set, `lunate --version` and the interactive welcome header SHALL both print the active home as `home <path> (LUNATE_HOME)`, and neither SHALL print it when the variable is unset. The trust store, the extension cache, user extensions, sessions, `auth.json`, `settings.json`, history, exports, the model catalog, Lunate's own user skills and recordings SHALL follow the root; `~/.agents/skills`, `~/.agents/mcp.json` and NuGet's packages folder SHALL NOT, because they are shared conventions or content-addressed package storage rather than Lunate state. No log file is promised: extension logging is the terminal through `IExtensionLog`, and the session file is the transcript.

#### Scenario: A store inside the workspace is refused

- **GIVEN** `LUNATE_HOME` set to a directory inside the worktree, or equal to its root
- **WHEN** a session composes, or `lunate extensions build`, `lunate cache prune` or `lunate trust` runs there
- **THEN** the command fails with `LUNATE_HOME points inside the workspace; trust records there could be written by the repository`, and nothing is written to that path

#### Scenario: A sibling or unrelated store is accepted

- **GIVEN** `LUNATE_HOME` set to a sibling of the worktree, or to a directory inside no workspace
- **WHEN** a session or a store-writing subcommand runs
- **THEN** it proceeds and writes under that directory

#### Scenario: A redirected run is visible

- **GIVEN** `LUNATE_HOME` set to an absolute temporary directory
- **WHEN** `lunate --version` runs and an interactive session starts
- **THEN** both print `home <that directory> (LUNATE_HOME)` in the same spelling, and a run without the variable prints neither

#### Scenario: A session with an unusable profile writes only the root

- **GIVEN** `LUNATE_HOME` set to a temporary directory, and `HOME`/`USERPROFILE` pointing at a directory that does not exist
- **WHEN** a full session runs one turn with a tool call
- **THEN** it exits successfully, its session file is under the redirected root, and the nonexistent home is still nonexistent

### Requirement: Host assemblies are the ones an extension loads

A built single-file extension SHALL load into its own collectible context, with every dependency resolved from the exact asset its build manifest records for a locked package — never re-resolved — and from the build directory otherwise. `Lunate.Extensibility.Abstractions`, `Lunate.Agent`, `Microsoft.Extensions.AI.Abstractions` and `System.Text.Json` SHALL always resolve to the host's copies, including when a locked package ships its own and when a packaged extension's directory carries one: the copy SHALL be ignored, the load SHALL succeed, and exactly one line SHALL name what was dropped and who shipped it. That line SHALL be logged and SHALL appear on the extension's `/extensions` row (and as `ignoredCopy` in `--json`), because which copy is live is not something a user can see for themselves. The authoring template and the authoring page SHALL show the contract reference as `PrivateAssets="all"`, `ExcludeAssets="runtime"` and `Private="false"`, with one sentence saying the host owns that assembly.

#### Scenario: A copied Abstractions is ignored, not loaded twice

- **GIVEN** a locked package that ships `Lunate.Extensibility.Abstractions.dll`, and a second package that ships another copy
- **WHEN** the extension loads
- **THEN** the contract types in the loaded extension are the host's (the extension reports `typeof(IExtensionFactory).Assembly.Location` as the host's file, and its context holds no second Abstractions), the load succeeds, and exactly one line names both packages

#### Scenario: A packaged extension's own copy is ignored too

- **GIVEN** a packaged extension directory that contains a copy of the contract
- **WHEN** it loads
- **THEN** the host's copy is used and the row and the log name the directory once

#### Scenario: The template keeps the contract out of the output

- **GIVEN** the packaged-extension template
- **WHEN** its project file is read
- **THEN** the `Lunate.Extensibility.Abstractions` reference carries `PrivateAssets="all"`, `ExcludeAssets="runtime"` and `Private="false"`, and no copy of the DLL is written to the extension's output

### Requirement: The API gate is the version the build saw

An extension's compatibility SHALL be decided by the API version its build saw — the `apiVersion` a single-file build records in `build.json`, the `Lunate.Extensibility.Abstractions` assembly reference a packaged DLL carries, and the range a manifest declares — never by the presence of a DLL and never at the first call. A version above the host's `ExtensionApi.Current` SHALL refuse the load before any type of the extension is opened, with `requires Lunate ≥ x.y` naming both the required version and the running one; a version at or below it SHALL load, so a file compiled before an API bump keeps working. Discovery SHALL refuse a packaged manifest whose declared range the host cannot meet using the same wording when the range's floor is above the host. The contract assembly's own version SHALL equal `ExtensionApi.Current`, because a refusal that names a version no Lunate ships is worse than no refusal.

#### Scenario: At or below the host loads

- **GIVEN** a build recording `apiVersion` equal to the host's, and another recording an older one
- **WHEN** each loads
- **THEN** both load and neither reports `requires Lunate`

#### Scenario: Above the host is refused, naming both versions

- **GIVEN** a build recording `apiVersion` above the host's `ExtensionApi.Current`
- **WHEN** it loads
- **THEN** the load is refused with `requires Lunate ≥ <required> but this is Lunate <running>`, and no tool or command of the extension was registered

#### Scenario: A packaged DLL built against a newer contract is refused

- **GIVEN** a packaged extension whose assembly references `Lunate.Extensibility.Abstractions` at a version above the host's, while its manifest declares a compatible range
- **WHEN** it loads
- **THEN** the load is refused with `requires Lunate ≥ <that version>` before the factory is looked for, so a manifest cannot talk the host into opening code it cannot support

### Requirement: Extension states and reload

`/extensions` SHALL show each single-file extension in one of `not-built`, `restoring`, `built`, `stale`, `failed (trust|restore|compile)` or `invalid (no #:extension)`, with the reason on the same line for a failure. `/extensions reload <id>` — and the non-interactive `lunate extensions reload <id|path>`, which runs the same preflight, build and a register-only load — SHALL, in this order: preflight trust for the bytes on disk (asking the user, and stopping if they refuse); build against the lock bound to those bytes; and only then swap. A trust or build failure SHALL return with the old build still loaded, still callable, and still the build `builds.json` names. The swap SHALL cancel the old build's live invocations with a notice naming the extension, drain them within `ReloadDrainBound`, unload the old context, load the new build, and raise `ToolsChanged` exactly once after the new tool set is registered, so the list a model sees is never half-swapped.

#### Scenario: A reload that fails to compile keeps the old build

- **GIVEN** a loaded extension that is edited into a compile error
- **WHEN** `/extensions reload <id>` runs
- **THEN** the extension stays loaded at its previous build, its tool still answers, `builds.json` still names that build, and the reload reports the first diagnostic

#### Scenario: A refused trust question stops before the compiler

- **GIVEN** a loaded extension whose file was edited and whose trust question the user declines
- **WHEN** `/extensions reload <id>` runs
- **THEN** no compile runs, the old build stays loaded and callable, and the reload reports the refusal

#### Scenario: A live invocation is drained, not cut

- **GIVEN** an extension command in flight
- **WHEN** `/extensions reload <id>` runs
- **THEN** the invocation is cancelled with a notice naming the extension, it leaves before the context is disposed, and its cancelled result is discarded

#### Scenario: A handler that ignores its token cannot hold the reload

- **GIVEN** a handler blocked on a manual reset event with its token unobserved
- **WHEN** `/extensions reload <id>` runs
- **THEN** the drain expires at `ReloadDrainBound`, exactly one log line names the command and says it did not return within the bound, and the reload completes anyway

#### Scenario: A command that runs during the build finishes on the old build

- **GIVEN** a command executing while a reload is still inside its build
- **WHEN** the build finishes and the swap runs
- **THEN** the command's result came from the old build and is kept, `ToolsChanged` fires once after the new build is loaded, and the next call answers from the new build

### Requirement: The model never approves trust

Trust SHALL be a user decision only. The model MAY write or edit a single-file extension and MAY ask for a reload, but no tool, command or protocol path SHALL let it record a trust decision, suppress a prompt, or load a changed file whose digest is stale. A model-initiated reload of a changed file SHALL end at the trust prompt and wait for the user.

#### Scenario: A model-initiated reload stops at the prompt

- **GIVEN** a loaded extension the model has just edited
- **WHEN** the model requests a reload through its tool
- **THEN** the reload stops at the trust prompt, the changed file is not compiled, and the old build stays loaded until the user answers
