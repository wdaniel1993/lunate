# 0017 addendum — Extensions: formats, restore and trust

- Status: **Accepted — 2026-10-24** (maintainer sign-off recorded in the card `t_ad12ff92`)
- Date: 2026-10-24
- Relates to: ADR 0017 (extensibility architecture), ADR 0006 (Roslyn placement),
  ADR 0009 (memory budgets), ADR 0005 (startup budgets)
- Evidence: [extension formats spike](../docs/spikes/extension-restore.md) (probe and measurements)
- Approved new NuGet packages: `NuGet.Protocol`, `NuGet.Packaging`, `NuGet.Configuration`,
  `NuGet.Frameworks`, `NuGet.Versioning`, `NuGet.Resolver` (+ `NuGet.Credentials` when a
  credential-provider plugin is wired)

## Context

ADR 0017 fixes the extension contract, the collectible load context and the trust model for
in-process extensions, but it assumes an extension arrives as a directory with a compiled entry
assembly and an `extension.json`. Two formats are now in scope:

- a **single file** (`.lunate/extensions/*.cs`, `~/.lunate/extensions/*.cs`) authored on the spot,
  compiled in process with the Roslyn the repository already ships, with its declaration and its
  NuGet references in the source itself;
- a **NuGet package** for larger extensions with several files, assets and their own tests,
  installed by id.

Both need the same restore, lock, compile and cache work. That work is code execution and network
access on behalf of a repository, so it cannot be bolted onto the existing load loop without
changing the order of the trust decision. The measured facts behind the decisions are in the
spike report; this addendum records the decisions themselves.

## Decision

**Formats**

- A single file is `<name>.cs` whose **header carries the declaration**, parsed by the same small
  scanner as the package directives, before any compilation:

  ```csharp
  #:extension acme-tools@1.0.0
  #:tool fetch_issue
  #:command issues
  #:hook ToolCalling
  #:package Octokit@14.0.0
  ```

  The header is part of the source bytes, so it is covered by the trust digest: the declaration a
  user approved is the declaration that runs. Registration the header does not declare is
  **refused**; a declaration with no matching registration is **warned** — the same rule as every
  other declaration surface. The header mirrors every declaration field of `extension.json`
  (`tools`, `commands`, `hooks`, `services`, `capabilities`, `modelProviders`) one to one, and a
  test fails when the manifest grows a field that has no directive. Only the leading block is
  scanned — directives, comments and blank lines before the first C# token, the SDK's own rule; a
  `#:` line after code is an error naming its line. **There is no sidecar manifest for a single
  file**: one declaration source, no conflict rules. Packaged extensions keep `extension.json`.
- A packaged extension is a `.nupkg` with `extension.json`, an entry assembly and its dependencies;
  `lunate install <id>` installs it (its own card, after the single-file path).
- Both formats produce the same `ExtensionDescriptor` and load through the same loop. Only the
  artifact source differs.

**Directive scanner**

- `#:package Name@Version` is parsed by **our own scanner**, matching the .NET 10 file-based-app
  directive syntax exactly, including NuGet version ranges. No SDK code is pulled into the host.
- `#:sdk`, `#:property` and `#:project` are **refused** with a message that names the directive and
  says why: they change how the file is built, which is the host's decision, not the extension's.
- The accepted grammar is pinned by tests against the documented .NET 10 syntax, so a future SDK
  change is a test failure rather than a silent divergence.

**One restore and cache component**

- Restore uses the NuGet client libraries in process. The dotnet SDK is not required and is never
  on the startup path; a refused package that only delivers through MSBuild points at the packaged
  format instead of silently invoking an SDK that may not exist.
- **The libraries load only when a restore runs.** Startup cost is image size, not eager
  initialisation; the startup budgets of ADR 0005 are unchanged and the spike's +8 ms is treated as
  a size effect until the measurement says otherwise (see the report's verification note).
- Sources come from the standard NuGet configuration chain: machine, user
  (`~/.nuget/NuGet/NuGet.Config`), project (`nuget.config` beside the extension or above it).
  Credentials come from `packageSourceCredentials` (including environment-expanded values) and,
  later, non-interactive credential-provider plugins. There is no interactive credential prompt in
  the host.
- A lock file is written at install. For a packaged extension it sits beside the installed package;
  for a single file it is `~/.lunate/cache/extensions/<id>/extension.lock.json` — the extension's
  cache entry, in **both** scopes (project and user), never beside the user's source, because a
  sidecar in the extensions directory would make "one file" false again. The lock has two halves:
  the **graph** (the sources, and per package the exact version, nupkg SHA-512, compile/runtime/
  native asset lists and dependency edges) and the **toolset** (the target TFM and RID, the exact
  runtime, and the reference pack's version and nupkg hash). Floating versions resolve once; later
  loads read the lock only; updating is explicit and rewrites the lock.
- Compilation uses Roslyn with the .NET reference assemblies matching the running runtime, the
  host contract assemblies, and package `ref/` or `lib/` assets. Framework assemblies and the
  shared contract assemblies always win over a package's copy.
- **The reference pack comes from NuGet, not from an installed SDK.** A published self-contained
  single-file binary has no SDK and no reference pack beside it, so `Microsoft.NETCore.App.Ref`
  (runtime version, RID) is restored through the same in-process engine into the global packages
  folder on first compile and reused. Shipping the pack inside the binary is the alternative;
  phase 2 reports its size, and a test compiles with the published binary, an empty `HOME` and no
  `dotnet` on `PATH`.
- **Package analyzers and source generators are off by default.** They are build-time code
  execution and most extensions never need them. `#:generators on` opts in, is part of the digest,
  and only then does the prompt carry the build-time-code line; `/extensions` names a package whose
  generators were skipped. This also removes the spike's 4.1 s analyzer cost from the common case.

**Cache**

- **Restored packages go to NuGet's global packages folder** (`NUGET_PACKAGES` when set, otherwise
  `~/.nuget/packages`). It is content-addressed and immutable, so it is shared across extensions and
  with the user's SDK cache, and it is not Lunate's to prune by size.
- **Per extension Lunate keeps only the compiled assembly and the lock file**, under
  `~/.lunate/cache/extensions/<id>/` — the builds in a `<key>/` subdirectory, the lock beside them.
  The key is a hash of the source, the lock (both halves), the API version, the TFM and the RID, so
  a graph change and a toolset change both produce a new build rather than reusing an old one.
- A successful compile **keeps the last two builds per extension** and deletes older ones, because
  every edit in the agent loop would otherwise leave a build behind.
- **Uninstall or removal deletes that extension's compiled outputs.** `lunate cache prune` removes
  orphaned entries; size-based pruning of the shared folder is out of scope by design.

**Trust before restore**

- Discovery parses the declaration and computes the trust digest before any network request and
  before any compile:
  `sha256("lunate-extension-v2" | id | [package id@version] | source bytes | the lock's graph)`.
  A file with no lock yet uses the same layout with no graph segment under the older tag
  `lunate-extension-v1`, so an unchanged file keeps the decision it already has. For a single file
  the source bytes **include the header directives** and any sidecar manifest.
- **The first trust has no lock yet.** It covers the id, the declared packages as written
  (including ranges) and the source. The first successful restore writes the lock and **binds its
  graph into the trust record automatically** — a restore downloads but executes nothing, so this
  is safe as long as compile only ever runs against the bound lock. From then on the digest
  includes the lock's **graph** half — the packages with their exact versions, hashes, resolved
  assets and edges — so changing the resolution re-prompts even when the source is untouched. The
  lock's **toolset** half (TFM, RID, runtime, reference pack and its hash) is deliberately outside
  the digest: it is integrity-checked, it keys the build cache, and a .NET patch that moves it
  causes a silent rebuild rather than a trust question with no decision in it. Source bytes are
  covered as written, never re-serialized; the graph is covered by its canonical bytes, which are
  the writer's own.
- The prompt names the extension, its declared packages **"and their dependencies"** — the
  transitive set is unknowable before a restore — and, only when the header opted in, that
  compiling runs package analyzers and source generators. A stale or absent approval stops before
  restore. After a restore, `/extensions` shows the resolved graph from the lock.
- **No tool of the extension surface approves trust.** It may write or edit the file and ask for a
  reload; the reload path records nothing, suppresses no prompt, and loads no stale digest, so a
  model-initiated reload of a changed file ends at the prompt. `lunate trust` is the CLI path that
  records a decision, and a process with the user's shell can run it — the approval panel, not the
  record, is what stands in front of that command (ADR 0017, amendment 2026-10-26).
- A cache hit re-checks the digest before loading the compiled assembly. Content-addressable
  caching does not weaken the trust decision.
- **The stale-digest-zero-feed-requests measurement stays in the suite permanently**: it is the
  test that proves trust precedes the network, not merely the load.

**Isolation**

- Every extension keeps its collectible `AssemblyLoadContext`. `Lunate.Extensibility.Abstractions`,
  `Lunate.Agent`, `Lunate.Ai`, `Microsoft.Extensions.AI.Abstractions` and `System.Text.Json` always
  resolve from the host; every other dependency resolves from the extension directory.
- **A package that ships its own copy of `Lunate.Extensibility.Abstractions` is accepted at install
  and always ignored at load** — the host's copy wins, and the same rule applies to the MEAI
  abstractions. Compatibility is decided by the **Abstractions version the extension references**,
  not by whether a DLL is present: a reference above the host's `ExtensionApi` version is refused
  with *"requires Lunate ≥ x.y"*. The extension template and the authoring docs show the correct
  reference (`PrivateAssets="all"`, `ExcludeAssets="runtime"`), and `/extensions` shows a one-line
  warning when a copy was ignored, so the author fixes the package rather than shipping the copy.
- The cache key includes the host API version so a host upgrade recompiles rather than silently
  binding against a different contract.
- RID-specific native assets are selected for the host RID and copied next to the entry assembly.
  The cache is RID-keyed; assets for other RIDs are not installed.
- A package whose only delivery is MSBuild targets (`build/`, `buildTransitive/` or
  `buildMultiTargeting/` with no assemblies for the TFM) is refused with a message that names the
  package and the target. A package that has assemblies plus targets is allowed, and the ignored
  targets are recorded. Only the framework-nearest build folder counts; `_._` placeholders do not
  trigger a refusal.
- A failed restore, compile or load is a `failed` row in `/extensions` with the first diagnostic
  (`file:line`) or the restore reason. No extension failure crashes the session.

**Timing**

- Startup reads declarations only. Restore and compile run at install or on first use, after the
  UI is ready, with visible progress and cancellation; they may take as long as they need.
- A cache hit loads within the existing startup budgets and must not load Roslyn or NuGet. The
  spike measured 3 ms for managed single files, 59 ms for a packaged extension and ~30 ms with
  native assets; all three loaded with zero Roslyn and zero NuGet assemblies in the process.

**Reload and restart**

- An in-process extension reloads through the command lease rules shipped with the command work:
  preflight trust, build the new artifact first, cancel live command invocations, show a notice,
  wait for the bounded drain, dispose registrations, unload the ALC, load the new build and fire
  `ToolsChanged`. A compile failure keeps the previous build running and reports the failure.
- File watching is optional and debounced. It never auto-approves; a changed file re-prompts.
- A process restart remains the fallback when an ALC cannot unload (a leaked static or a native
  library that cannot be released).

## Alternatives considered

- **Require the dotnet SDK and shell out to `dotnet build`.** Rejected: the shipped binary is
  self-contained and the SDK may be absent; on a machine with a warm SDK the measured difference
  is within 0.1 s for a package-free file and the in-process path is faster for a small package
  graph. The SDK stays an explicit escape hatch at most, never an automatic fallback.
- **Parse the declaration from an attribute with Roslyn.** Rejected: it costs ~47 ms and loads
  Roslyn on discovery and on every cache hit, for a declaration a header scanner reads in
  microseconds.
- **An always-sidecar manifest (or an inferred one).** Rejected: a sidecar splits "one file" into
  two, and inference cannot express tools or hooks. Header directives keep the file self-describing
  and let the declaration ride inside the same trusted bytes.
- **Treat the lock file as advisory and re-resolve on every load.** Rejected: it makes loads
  network-dependent and lets a floating version change the code that runs without a trust
  decision.
- **Trust after restore, before load.** Rejected: restore downloads code and compile runs package
  analyzers and source generators; approval that arrives after either is too late.
- **Prune the shared package folder by size.** Rejected: it is NuGet's content-addressed cache,
  shared with the SDK; deleting from it is surprising and outside Lunate's ownership.
- **Refuse every package with MSBuild files.** Rejected: it would refuse common dependencies such
  as `Microsoft.Extensions.Logging.Abstractions`, whose targets only filter an analyzer. The
  refusal is about delivery mechanism, not file presence.
- **Load all dependencies from the extension's package graph, including `System.Text.Json`.**
  Rejected: two `System.Text.Json` identities in one process break the host's contract and the
  message/JSON boundary; the host's copy is the contract.

## Consequences

- New NuGet packages enter the product, with the size and startup cost recorded in the spike
  (+15.8 MiB single file, +19 MB RSS after the UI is ready, +8 ms startup pending the size check).
  The component is lazy; a user who installs no extensions pays only the image size.
- Restoration needs writable cache space and a network on first install; the offline story is a
  restored cache plus an explicit update action.
- Per-extension cache footprint drops to the compiled assembly plus the lock, plus at most two
  builds per extension — the restored packages are shared with the SDK cache.
- Credential-provider plugins are not covered by this decision. The first release supports config
  credentials only and fails with an actionable message for feeds that need more.
- Windows and Linux RID selection, package signature verification and cache eviction are
  follow-ups; the spike measured only `osx-arm64`.
- The single-file format needs documentation and a template before the agent loop can author one
  reliably; the model wrote a valid file without either, but that is not a contract.
- `/extensions` gains states for `not-built`, `restoring`, `failed (restore|compile|trust)` and
  `stale` (source changed, reload pending), in addition to the existing `loaded` / `trust-denied`.
