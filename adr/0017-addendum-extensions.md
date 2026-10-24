# 0017 addendum — Extensions: formats, restore and trust

- Status: Draft — pending maintainer sign-off
- Date: 2026-10-24
- Relates to: ADR 0017 (extensibility architecture), ADR 0006 (Roslyn placement),
  ADR 0009 (memory budgets)
- Evidence: [extension formats spike](../docs/spikes/extension-restore.md) (probe and measurements)
- Requires sign-off on new NuGet packages: `NuGet.Protocol`, `NuGet.Packaging`,
  `NuGet.Configuration`, `NuGet.Frameworks`, `NuGet.Versioning`, `NuGet.Resolver`

## Context

ADR 0017 fixes the extension contract, the collectible load context and the trust model for
in-process extensions, but it assumes an extension arrives as a directory with a compiled entry
assembly and an `extension.json`. Two formats are now in scope:

- a **single file** (`.lunate/extensions/*.cs`, `~/.lunate/extensions/*.cs`) authored on the spot,
  compiled in process with the Roslyn the repository already ships, with NuGet references declared
  in the source (`#:package Name@Version`);
- a **NuGet package** for larger extensions with several files, assets and their own tests,
  installed by id.

Both need the same restore, lock, compile and cache work. That work is code execution and network
access on behalf of a repository, so it cannot be bolted onto the existing load loop without
changing the order of the trust decision. The measured facts behind the decisions are in the
spike report; this addendum records the decisions themselves.

## Decision

**Formats**

- A single-file extension is `<name>.cs` plus an optional sidecar `<name>.json` using the existing
  manifest schema (`id`, `version`, `apiVersion`, `tools`, `hooks`, `services`). Without a
  sidecar, the id is the file stem, the version is `0.0.0-local` and the declaration is
  discovered at load.
- A packaged extension is a `.nupkg` with `extension.json`, an entry assembly and its dependencies;
  `lunate install <id>` installs it (the install command is a follow-up card). Packages keep the
  manifest inside the package.
- Both formats produce the same `ExtensionDescriptor` and load through the same loop. Only the
  artifact source differs.

**One restore and cache component**

- Restore uses the NuGet client libraries in process. The dotnet SDK is not required and is never
  on the startup path; a refused package that only delivers through MSBuild points at the packaged
  format instead of silently invoking an SDK that may not exist.
- Sources come from the standard NuGet configuration chain: machine, user (`~/.nuget/NuGet/NuGet.Config`),
  project (`nuget.config` beside the extension or above it). Credentials come from
  `packageSourceCredentials` (including environment-expanded values) and, later, non-interactive
  credential-provider plugins. There is no interactive credential prompt in the host.
- A lock file is written next to the extension at install (`extension.lock.json`, or
  `<name>.lock.json` for a single file). It records the target TFM and RID, the sources, and per
  package the exact version, nupkg SHA-512, compile/runtime/native asset lists and dependency
  edges. Floating versions resolve once; later loads read the lock only; updating is explicit and
  rewrites the lock.
- Compilation uses Roslyn with the targeting pack's reference assemblies for the host TFM, the
  host contract assemblies, package `ref/` or `lib/` assets, and package analyzers and source
  generators from the nearest `analyzers/dotnet/roslyn*/cs` folder. Framework assemblies and the
  shared contract assemblies always win over a package's copy.
- The cache lives under the user store: `~/.lunate/cache/extensions/<id>/<key>/bin/…` with a shared
  `~/.lunate/cache/packages/<id>/<version>/…`, keyed by source hash, lock hash, API version, TFM and RID. A
  cache entry is reused only when the source digest still matches.

**Trust before restore**

- Discovery parses the declaration and computes the trust digest before any network request and
  before any compile:
  `sha256("lunate-extension-v1" | id | [package id@version] | source bytes | lock-file bytes)`.
  For a single file the source bytes include the sidecar manifest.
- The digest covers the dependency set through the lock: a changed lock re-prompts even when the
  source is untouched. It covers bytes as written, never a re-serialization.
- The prompt names the extension, its scope and that compiling it runs package analyzers and
  source generators. A stale or absent approval stops before restore.
- A cache hit re-checks the digest before loading the compiled assembly. Content-addressable
  caching does not weaken the trust decision.

**Isolation**

- Every extension keeps its collectible `AssemblyLoadContext`. `Lunate.Extensibility.Abstractions`,
  `Lunate.Agent`, `Lunate.Ai`, `Microsoft.Extensions.AI.Abstractions` and `System.Text.Json` always
  resolve from the host; every other dependency resolves from the extension directory.
- An extension's copy of a host-shared assembly is never loaded. A package graph that brings a
  different version of a shared assembly is bound to the host version; the cache key includes the
  host API version so a host upgrade recompiles rather than silently binding against a different
  contract.
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
  Roslyn on discovery and on every cache hit, for a declaration the JSON parser reads in 24 µs.
- **Treat the lock file as advisory and re-resolve on every load.** Rejected: it makes loads
  network-dependent and lets a floating version change the code that runs without a trust
  decision.
- **Trust after restore, before load.** Rejected: restore downloads code and compile runs package
  analyzers and source generators; approval that arrives after either is too late.
- **Refuse every package with MSBuild files.** Rejected: it would refuse common dependencies such
  as `Microsoft.Extensions.Logging.Abstractions`, whose targets only filter an analyzer. The
  refusal is about delivery mechanism, not file presence.
- **Load all dependencies from the extension's package graph, including `System.Text.Json`.**
  Rejected: two `System.Text.Json` identities in one process break the host's contract and the
  message/JSON boundary; the host's copy is the contract.

## Consequences

- New NuGet packages enter the product, with the size and startup cost recorded in the spike
  (+15.8 MiB single file, +8 ms startup, +19 MB RSS). The component is lazy; a user who installs
  no extensions pays only the image size.
- Restoration needs writable cache space and a network on first install; the offline story is a
  restored cache plus an explicit update action.
- Credential-provider plugins are not covered by this decision. The first release supports config
  credentials only and fails with an actionable message for feeds that need more.
- Windows and Linux RID selection, package signature verification and cache eviction are
  follow-ups; the spike measured only `osx-arm64`.
- The single-file format needs documentation and a template before the agent loop can author one
  reliably; the model wrote a valid file without either, but that is not a contract.
- `/extensions` gains states for `not-built`, `restoring`, `failed (restore|compile|trust)` and
  `stale` (source changed, reload pending), in addition to the existing `loaded` / `trust-denied`.
