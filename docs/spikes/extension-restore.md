# Extension formats spike — single-file and packaged extensions

- Date: 2026-10-24
- Branch: `spike/extension-restore` (off `main` at `2956e572`)
- Status: spike complete; recommendations are proposals, not an accepted design
- Relates to: [ADR-0017](../../adr/0017-extensibility-architecture.md), the draft
  [addendum](../../adr/0017-addendum-extensions.md), the packaged-install follow-up
- Probe: `/tmp/extspike` on the spike machine (not committed); all numbers below come from it

## Question

Two extension formats share one restore-and-cache component:

1. **Single file** — `.lunate/extensions/*.cs` (project) and `~/.lunate/extensions/*.cs` (user),
   compiled in process with the Roslyn the repository already ships, with NuGet references
   declared through `#:package Name@Version`.
2. **NuGet package** — a packaged extension installed by id, for larger extensions with several
   files, assets and their own tests.

This spike answers: which restore engine, which feeds and credentials, what the lock file holds,
where trust sits relative to restore and compile, how isolation behaves, what happens offline,
how long each path takes, what the manifest is, how errors surface, how reload works, and whether
the product can already do the agent-loop demo.

## Method

A throwaway console probe (`net10.0`, osx-arm64) did the whole pipeline in one process:

- **Restore** with the NuGet client libraries (`NuGet.Protocol` 7.9.0, `NuGet.Packaging`,
  `NuGet.Configuration`, `NuGet.Frameworks`, `NuGet.Versioning`): sources from
  `Settings.LoadDefaultSettings`, flat-container downloads, SHA-512 per package, nuspec-driven
  dependency walk, extraction into a private cache.
- **Lock** written next to the extension (exact versions, package SHA-512, compile/runtime/native
  asset lists, dependency edges, sources, TFM, RID).
- **Compile** with Roslyn 5.9.0 (`CSharpCompilation`, reference assemblies from the installed
  `Microsoft.NETCore.App.Ref` pack, host contract assemblies, package `ref/` or `lib/` assets,
  package analyzers and source generators from the nearest `analyzers/dotnet/roslyn*/cs` folder).
- **Load** the emitted assembly into a collectible `AssemblyLoadContext` that shares the host
  contract assemblies, and instantiate or probe the factory.

Each cold run used a fresh extension cache and a fresh `NUGET_HTTP_CACHE_PATH`; each cache-hit run
was a fresh process with a blackholed proxy (`http_proxy=http://127.0.0.1:9`) to prove no network
use. Three runs per cell; the table reports min/median/max.

Commands timed (the probe and its sources live under `/tmp/extspike`, not in the repository):

```bash
# cold restore + compile + load, fresh --cache and fresh NUGET_HTTP_CACHE_PATH per run
dotnet /tmp/extspike/probe/bin/Release/net10.0/Probe.dll run \
  --spec /tmp/extspike/specs/<case>/spec.json --cache /tmp/extspike/timing-cache \
  --refs ~/.dotnet/packs/Microsoft.NETCore.App.Ref/10.0.13/ref/net10.0 \
  --hostrefs /tmp/extspike/hostrefs

# cache-hit load, fresh process, http_proxy/https_proxy pointed at 127.0.0.1:9
dotnet /tmp/extspike/probe/bin/Release/net10.0/Probe.dll load \
  --spec /tmp/extspike/specs/<case>/spec.json --cache /tmp/extspike/timing-cache \
  --hostrefs /tmp/extspike/hostrefs

# SDK comparison, fresh NUGET_PACKAGES per run
dotnet build -c Release --nologo /tmp/extspike/cli/<case>/<case>.csproj

# startup and size of the two published single-file apps
hyperfine --warmup 2 --runs 15 ./out-base/sizespike
hyperfine --warmup 2 --runs 15 ./out-nuget/sizespike
```

**Machine state.** The whole matrix ran while another worker's full build/test gate was running on
the same Mac: load average **28.4–36.6** (1-minute 28–31) for every run. Absolute times are
inflated and noisy; the medians are still useful for order-of-magnitude and relative comparisons.
Network was warm (nuget.org reachable, DNS/TLS and OS page cache warm; the NuGet HTTP cache was
cold per run). SDK 10.0.402, runtime 10.0.13, Roslyn 5.9.0, NuGet 7.9.0, TFM `net10.0`, RID
`osx-arm64`.

## Restore engine: NuGet libraries in process or the dotnet SDK

### Measurements

In-process pipeline (restore + compile, one process, fresh cache; ms, min/median/max):

| Case | Restore | Compile | Total |
| --- | --- | --- | --- |
| (a) one tool, no packages | 84 / **101** / 106 | 1 409 / **1 868** / 1 967 | 1 615 / **2 032** / 2 258 |
| (b) `NJsonSchema` 11.6.1 + `Newtonsoft.Json` 13.* (4 packages) | 927 / **978** / 1 185 | 1 533 / **1 534** / 1 671 | 2 536 / **2 596** / 2 917 |
| (b′) same plus a source generator (`Microsoft.Extensions.Logging.Abstractions` 10.0.0; single run) | 853 | 5 825 (analyzer 4 149) | 6 900 |

SDK path, same sources and package sets, `dotnet build -c Release` with a fresh `NUGET_PACKAGES`
(fresh restore + compile; ms, min/median/max):

| Case | Total |
| --- | --- |
| (a) one tool, no packages | 1 867 / **1 939** / 2 209 |
| (b) two packages (4-package graph) | 2 752 / **3 049** / 3 081 |

The SDK was warm (MSBuild and compiler servers already running for the other worker). On this
machine the in-process engine is within 0.1 s of the SDK for (a) and ~0.45 s faster for (b); on a
cold SDK the first build was 6.3 s. The SDK path also requires an SDK: this machine has three
(10.0.103, 10.0.401, 10.0.402), but the shipped binary is self-contained and a user need not have
one.

### What the libraries cost

Two minimal self-contained single-file R2R apps (`osx-arm64`), one with the NuGet references:

| Measurement | Without NuGet | With NuGet | Delta |
| --- | --- | --- | --- |
| Single-file size | 93 737 098 B (89.4 MiB) | 110 343 306 B (105.2 MiB) | +16 606 208 B (+15.8 MiB, +17.7 %) |
| Startup median (hyperfine, 15 runs) | 40.6 ms | 48.7 ms | +8.1 ms (+20 %) |
| Process wall with first NuGet API use | — | 101.4 ms median (86.1–143.0) | +52.7 ms over the 48.7 ms startup |
| Peak RSS at startup | 104.2 MB | 123.0 MB | +18.8 MB |

The NuGet assembly set itself is 3.6 MB on disk (`NuGet.Protocol` 1.1 MB, `NuGet.Packaging`
0.7 MB, `Newtonsoft.Json` 0.7 MB and smaller satellites); the rest of the size and RSS delta is
bundle/R2R image mapping of the extra assemblies. Referencing the libraries does **not** load them
at startup; the 8 ms is packaging and image mapping, not JIT. First use costs ~53 ms once, and
only on the restore path, which is off the startup path by the timing rule.

### Decision

**Use the NuGet client libraries in process. Do not require the SDK and do not put it on the
critical path.** The measured gap does not justify a second engine, and the SDK cannot be assumed
present in a self-contained install. The SDK remains a possible *explicit* escape hatch later for
packages that only deliver through MSBuild (see [Isolation](#isolation)), never an automatic
fallback.

## Feeds

| Case | Result |
| --- | --- |
| nuget.org | Works (all packages above). |
| Project `nuget.config` | Works: `local` + nuget.org in one restore; sources are read from the extension directory upward. |
| User `~/.nuget/NuGet/NuGet.Config` merged with a project config (no `<clear/>`) | Works: the probe reported `sources: ["local", "nuget.org"]` and restored `Newtonsoft.Json` from nuget.org. |
| Local folder feed | Works, but **flat only**: `LocalFolderUtility` finds `*.nupkg` at the feed root; the hierarchical `<id>/<version>/<id>.<version>.nupkg` layout was invisible. The product must support both (flat folder feeds and a packages-folder-style cache). |
| Private HTTP feed, basic auth from `nuget.config` with `%ENV%` password | Works: 300 ms restore of one package; the service index and flat container were served with `Authorization: Basic`. |
| Wrong password | Fails, but the message is poor: `restore failed: package 'private.spike.lib' was not found on private,nuget.org (private: FatalProtocolException: Unable to load the service index for source http://127.0.0.1:8765/v3/index.json.)`. The 401 is in the inner exception and must be surfaced as `authentication failed for feed 'private' (401); check packageSourceCredentials or the credential provider.` |
| Credential provider plugins | **Not proven.** `~/.nuget/plugins` does not exist on this machine, so no plugin was available to exercise. Config-based credentials work; the plugin path (`NuGet.Credentials` + the plugin protocol) needs a real provider and is unmeasured. Interactive browser/device-code prompts are not viable in a terminal host that must not block the UI; the plan should be non-interactive providers only. |

Feed parsing itself is cheap: the first `PackageSource`/`FindPackageByIdResource` touch was
~53 ms once per process, and per-restore source setup is inside the 84–106 ms no-package restore.

## Lock file

The lock file lives next to the extension:

- single file: `hello.cs` → `hello.lock.json` (0.2 KB with no packages, 2.0 KB for the schema case)
- package: `<extension-dir>/extension.lock.json` (1.1 KB for the packaged case)

It records the target (`net10.0`, `osx-arm64`), the sources used, and per package: exact version,
base64 SHA-512 of the nupkg, compile/runtime/native asset paths, and dependency edges. Floating
ranges resolve once at install: `Newtonsoft.Json` `13.*` resolved to **13.0.4** and stayed there.
Later loads read the lock only. Updating is an explicit action (re-resolve, write a new lock,
which changes the trust digest and re-prompts).

Two findings from building it:

- The digest must cover the lock **bytes as written**, not a re-serialization. The first probe
  version hashed a re-serialized `LockFile` object and silently ignored a modified lock; hashing
  the file bytes fixed it (digests below).
- The lock is an integrity record, not a signature. On install the downloaded nupkg's SHA-512 is
  recorded; later loads trust the cache directory (the same trust boundary as
  `~/.nuget/packages`). Re-verifying every package file on every load would cost more than the
  3 ms budget allows and is not proposed.

## Trust ordering

### The digest

```text
sha256(
  "lunate-extension-v1" 0 id 0
  [ package-id "@" package-version 0 ]        # packages only
  source bytes 0
  lock-file bytes
)
```

For a single file the source bytes are the `.cs` file (plus the sidecar manifest once it exists);
for a package they are the `.nupkg` bytes. The digest covers the dependency set through the lock,
so a changed dependency re-prompts even when the source is untouched.

Measured sensitivity (same id, SHA-256):

| Change | Digest |
| --- | --- |
| `hello` with its lock | `27a2c061…` |
| `hello` source with one comment appended | `dda4585e…` |
| `hello` with the lock removed | `cceefc2e…` |
| `schema` with its lock | `a236f505…` |
| `schema` lock with `Newtonsoft.Json` bumped 13.0.3 → 13.0.4, source untouched | `474efa05…` |

### Trust precedes restore and compile

The probe enforces the order discovery → parse declaration → compute digest → trust decision →
restore → compile → load. Passing a stale approval (`--trusted deadbeef`) produced:

```text
{"phase":"failed","stage":"trust","message":"extension 'private' changed since it was trusted; approve it again."}
```

with **zero new requests** to the authenticated feed (request log 20 → 20) and an empty cache
directory. That is the property the design needs: a stale or absent approval stops before any
package is downloaded and before any package code can run.

Compile-time code execution is real, not theoretical: a package source generator ran during
compilation, produced 1 generated syntax tree and took **4 149 ms** of the 5 825 ms compile. With
the generator suppressed the same source fails with `CS8795` (partial method without an
implementation), which is a clean proof that the generator's code executed. Analyzers and
generators from referenced packages therefore belong **inside** the trust boundary, not after it.

Trust must also gate a cache hit: the loader recomputes the digest from the source and lock before
it touches the compiled assembly. A changed source with an old cache entry is a cache miss, not a
load.

## Isolation

Each extension loads into its own collectible `AssemblyLoadContext`. The host shares
`Lunate.Extensibility.Abstractions`, `Lunate.Agent`, `Lunate.Ai`,
`Microsoft.Extensions.AI.Abstractions` and `System.Text.Json` from the default context; the ALC
returns `null` for those names and every other dependency comes from the extension's own directory.

| Case | Measured behaviour |
| --- | --- |
| (i) Package brings `System.Text.Json` 9.0.0 while the host has 10.0.13 | Restore records the package, but compile references the host's targeting-pack `System.Text.Json` (the package's copy is deduplicated by simple name), and at run time the extension observed **10.0.0.0**. There is no side-by-side: the host wins silently. That is correct for a semver-compatible contract, and a hard `MissingMethodException`/`TypeLoadException` at first call if the host's version is older than what the extension compiled against. Mitigation: the cache key includes the host API version, so a host upgrade recompiles; the extension's copy of a shared assembly is never loaded. |
| (ii) RID-specific native assets (`SkiaSharp` 4.153.1 + `SkiaSharp.NativeAssets.macOS`) | Works. The lock selected `runtimes/osx/native/libSkiaSharp.dylib` (15.8 MB) and the runtime copied it next to the entry assembly; the extension ran `SKBitmap`/`SKImage.Encode` and returned a 95-byte PNG. Cache-hit load 25/30/32 ms, working set +8.9–9.1 MB. The cache is RID-keyed: a cache built on `osx-arm64` is not reused on another RID. |
| (iii) Package that relies on MSBuild targets | Refused when the package has **no assemblies** for the TFM. `Grpc.Tools` 2.84.0 produced: `extension 'targets' references 'Grpc.Tools' 2.84.0, which delivers its content through MSBuild targets (build/Grpc.Tools.props) and has no assemblies for net10.0. In-process compilation cannot run MSBuild; remove the dependency or install a packaged extension built outside Lunate.` |
| (iii′) Package with assemblies **and** targets | Allowed, with a recorded warning. `Microsoft.Extensions.Logging.Abstractions` 10.0.0 ships `buildTransitive/net8.0/…targets` that only removes a roslyn4 analyzer; its assemblies are present, so the targets are ignored and the compile proceeds (`build-assets-ignored`). `System.Text.Json` 9.0.0 behaves the same way. |

The distinction matters: a blanket refusal of every package with a `build/` folder would reject
`Microsoft.Extensions.Logging.Abstractions`, one of the most common dependencies in .NET. The rule
is **"MSBuild is its delivery mechanism"**, not "MSBuild files exist". It also has to select the
*nearest TFM* build folder: `Microsoft.Extensions.DependencyInjection.Abstractions` has
`buildTransitive/netcoreapp2.0/…targets` and a `net10.0/_._` placeholder, and the placeholder must
not trigger a refusal.

## Offline and failure states

- A restored cache works with no network: the three matrix cache-hit runs used a blackholed proxy
  and loaded (3 ms managed, 59 ms packaged); the native case loaded in 25–32 ms in separate
  offline-equivalent runs (the probe never opens a socket on the load path).
- No lock and no network:
  `restore failed: 'hello' has no lock file at '…/hello.lock.json' and the network is disabled.`
- Lock present but the cache is empty:
  `restore failed: package 'Namotion.Reflection' 3.5.0 is not in the cache and the network is disabled.`
- A failed restore is a row in `/extensions`, not a crash: the probe exits 1 with a structured
  `{"stage":"restore","message":…}` and the host maps the first sentence to
  `failed: restore failed: …`. The existing rule (a load failure is contained per extension) holds.

## Measurements

State machine used below:

| State | Cache entry | Lock | Network |
| --- | --- | --- | --- |
| cold | absent | absent | reachable, HTTP cache cold per run |
| cache hit | present, source hash matches | present | blackholed |
| offline miss | absent | present | blackholed |

Three runs per cell; ms as min/median/max. `load` is in-process, excluding host startup.

| Case | Cold restore | Cold compile | Cold total | Cache-hit load | Cache-hit GC delta | Cache-hit working set |
| --- | --- | --- | --- | --- | --- | --- |
| (a) one tool, no packages | 84 / **101** / 106 | 1 409 / **1 868** / 1 967 | 1 615 / **2 032** / 2 258 | 3 / **3** / 3 | 38.8 KB | +1.0–1.2 MB |
| (b) two packages (4-package graph) | 927 / **978** / 1 185 | 1 533 / **1 534** / 1 671 | 2 536 / **2 596** / 2 917 | 3 / **3** / 3 | 38.8–57.3 KB | +1.0–1.2 MB |
| (c) packaged extension (2 packages) | 1 004 / **1 084** / 1 103 | 0 / **0** / 1 (copy) | 1 068 / **1 161** / 1 173 | 57 / **59** / 73 | 84.1–100.6 KB | +12.4–12.9 MB |

Additional points:

- Generator case: 6 900 ms total, 5 825 ms compile, 4 149 ms analyzer/generator.
- Cache-hit process contained **0** `Microsoft.CodeAnalysis.*` and **0** `NuGet.*` assemblies in
  all three cases — "Roslyn loads on first use" survives the cache-hit path by measurement.
- Cache footprint per extension as measured: (b) 22.4 MiB packages + 2.0 MiB bin; (c) 20.3 MiB
  packages + 0.7 MiB bin, most of it the fully extracted `Newtonsoft.Json` package. **Decided
  (2026-10-24):** the restored packages live in NuGet's content-addressed global packages folder
  (`NUGET_PACKAGES`, default `~/.nuget/packages`), shared with the SDK cache and not pruned by
  Lunate; per extension only the compiled assembly and the lock file are kept, the last two builds
  survive a recompile, and uninstall deletes them. The 22 MiB figure is therefore the *shared*
  folder's cost, not a per-extension one.
- Against today's budgets (150 ms local startup, 300 ms macOS CI): a managed cache hit (3 ms) and a
  packaged cache hit (59 ms) fit; even several extensions stay inside one startup-budget's worth.
  Native assets add ~30 ms and ~9 MB each. Restore/compile (1.2–2.9 s, 6.9 s with a generator) is
  an install-time cost, not a startup cost.

## Verification: the startup cost is image size, not eager loading

The +8 ms in the recommendation was measured on the spike's own probe. The maintainer asked for
confirmation that the libraries load only when a restore runs. Measured separately, same machine,
self-contained single-file `osx-arm64`, 20 runs each after one warm-up:

| Probe | Packages | Binary | Median startup | NuGet assemblies loaded at startup |
| --- | --- | --- | --- | --- |
| A | none | 36.4 MB | **70.1 ms** (61.3–81.8) | 0 |
| B | NuGet.Protocol, Packaging, Configuration, Frameworks, Versioning | 37.6 MB | **69.0 ms** (58.5–77.8) | **0** |

Both binaries print the same five assemblies in `AppDomain.CurrentDomain.GetAssemblies()`; no
`NuGet.*` assembly appears without a call into it, so the managed loader defers them as expected.
The difference between A and B is inside the run-to-run spread, which means the +8 ms was not
eager initialisation: it is file size plus measurement noise on a machine that was running a full
gate at the time. The image grows by the uncompressed assemblies (~16 MiB) only in the published
configuration the spike used; with the default compressed single file the same five packages add
~1.2 MB.

The ADR therefore records the cost as **image size**, keeps ADR 0005's startup budgets unchanged,
and adds a guard to the apply card: a startup test that fails if any `NuGet.*` assembly is loaded
before the first restore.

## Manifest

The declaration must exist before compilation because the trust prompt and the digest need it, and
because the host must know the id and declared surfaces before it runs any package code.

Measured parse costs for one small declaration (fresh process, 100 iterations where applicable):

| Shape | Cost | Roslyn loaded |
| --- | --- | --- |
| Sidecar JSON (same shape as `extension.json`) | 24 µs | no |
| Header block (`// lunate: …` line scan) | 4 µs | no |
| Attribute parsed with Roslyn syntax | 47.3 ms + 2 `Microsoft.CodeAnalysis` assemblies | yes |

**Maintainer decision (2026-10-24): header directives in the `.cs` file**, parsed by the same
small scanner as `#:package`, before any compilation:

```csharp
#:extension acme-tools@1.0.0
#:tool fetch_issue
#:hook ToolCalling
#:package Octokit@14.0.0
```

Neither an always-sidecar nor an inferred declaration: the header keeps "one file" true, and the
declaration rule holds because the header is part of the bytes the user trusted. A registration the
header does not declare is **refused**; a declaration with no matching registration is **warned** —
the same rule as every other declaration surface. A sidecar JSON stays optional for single files;
packaged extensions keep `extension.json`.

Why the declaration can be trusted before compilation:

- The header is scanned as text; no extension code runs to read it, and Roslyn is not loaded
  (4 µs against 47 ms for a syntax-tree attribute).
- It is inside the source bytes, so the digest covers declaration and code together: editing either
  re-prompts. A separate sidecar would have to be digested as a second file, and an inferred
  declaration cannot express tools or hooks at all.
- It cannot lie about registrations silently: after load the host cross-checks the declared tools,
  hooks and commands against what the factory registered, and an undeclared registration fails the
  load.

The scanner is ours, matching the .NET 10 directive grammar for `#:package` exactly (including
version ranges); `#:sdk`, `#:property` and `#:project` are refused with a message naming the
directive, because they change how the file is built and that is the host's decision.

## Compile errors

A syntax error in `broken.cs` surfaced as:

```text
{"phase":"failed","stage":"compile","file":"broken.cs:8","diagnostic":"…/broken.cs(8,27): error CS1026: ) expected","diagnostics":2}
```

The first diagnostic carries `file:line` and the error count is kept. `/extensions` should render
the first sentence, for example `failed: broken.cs:8 CS1026: ) expected (2 errors)`, with the full
diagnostic in the log. This is the same shape the manifest docs already promise for load failures,
so the view needs no new concept.

## Hot reload vs restart

The repository already has the right primitive: `CommandInvocationLeases` implements cancel →
in-flight ids → bounded drain, and unload disposes registrations before unloading the ALC. The
proposed semantics for an in-process extension:

1. **Preflight.** Recompute the digest; if it changed, prompt for trust *before* touching the
   running extension. A denied prompt leaves the old build running.
2. **Build first.** Restore/compile the new source into a new cache entry. Nothing is unloaded
   while the new build is being produced; a compile failure reports `failed: …` and keeps the
   previous build loaded (`still running the previous build`).
3. **Swap.** Cancel the extension's command leases, show a notice, wait for the bounded drain
   (the existing bound, default 2 s), then dispose services/registrations and unload the ALC.
4. **Load.** Load the new assembly, run the factory, cross-check declarations, fire `ToolsChanged`
   if the tool set changed.
5. **Report.** `/extensions` shows the new state; an unresponsive handler is named by command id
   after the drain expires, exactly as today.

`/extensions reload <id>` (or `/reload` with no argument for all changed) should be the explicit
command. Automatic file watching is optional and should be debounced; it must never auto-approve,
and a changed file always re-prompts. A full process restart is not required for reload; it stays
the fallback when the ALC does not unload (a leaked static or a native library that cannot be
unloaded).

## Agent loop demo

I ran the real product loop in a throwaway repository:

```bash
lunate -p --yolo "Create the file .lunate/extensions/hello.cs containing a minimal Lunate
extension that registers one tool named hello. …"
```

The loop wrote exactly one file, `.lunate/extensions/hello.cs`, with a correct
`HelloFactory : IExtensionFactory`, one `hello` tool and a handler. The product's own inspection
then showed only the global `subagents` extension:

```text
$ lunate extensions --json
[{"id":"subagents","version":"0.1.0","scope":"global","state":"loaded","loadMs":9,…}]
```

The file is invisible: discovery enumerates **directories** with `extension.json`, not `*.cs`
files. A directory containing `hello.cs` but no manifest is a discovery error
(`extension directory '…/hello' has no extension.json manifest.`). The spike compiler then took the
same file, compiled it in 1.7 s and loaded it in 3 ms (`factoryType: HelloFactory`), so the source
is valid and the missing half is entirely on the product side.

**What is missing, precisely:**

1. Discovery of `*.cs` as a descriptor kind (plus the sidecar manifest), with the same
   global-before-project ordering and duplicate-id rules.
2. The restore/lock/compile/cache component itself — no `src/` project references NuGet, and
   there is no compile path outside `Lunate.Roslyn`'s workspace loading.
3. A single-file trust section and prompt (the store already has `ComputeFileContentHash` for
   `mcp.json`, but no extension-file section and no `/extensions` row state for a source file).
4. `/extensions reload` (today only remote `restart` exists) and the reload state machine above.
5. Documentation and a template so the model knows the format; the loop guessed a correct shape
   here, but there is no spec, no `#:package` parsing and no sample to copy.

**Estimate:** four cards — (1) discovery + sidecar + trust, 1–2 days; (2) restore/lock/compile/
cache + `/extensions` states, 4–6 days; (3) reload via leases, 2–3 days; (4) packaged install and
hardening, 3–5 days. End-to-end single-file demo, without packaged install: roughly **8–11
developer days**. The demo cannot be completed today without faking the pipeline; the honest
status is "the agent can write it, the product cannot yet run it".

## Recommendation

**Engine.** Use the NuGet client libraries in process (`NuGet.Protocol`, `NuGet.Packaging`,
`NuGet.Configuration`, `NuGet.Frameworks`, `NuGet.Versioning`, `NuGet.Resolver`; add
`NuGet.Credentials` only when a provider plugin is actually wired). The probe resolved the small
graphs with a nuspec-driven walk; production should use `NuGet.Resolver` for full conflict
resolution, which is recommended but not separately measured here. Do not require the dotnet SDK
and do not probe for it on the hot path. The measured in-process cost is at parity with a warm SDK
build and lower for a package graph, while the SDK is absent on a self-contained install; the price
is +15.8 MiB of binary, +8 ms startup and +19 MB RSS, accepted for a component that runs after the
UI is ready. If a package is refused because MSBuild is its delivery mechanism, the message points
at a packaged extension built outside Lunate, not at an automatic SDK fallback.

**Lock file.** `extension.lock.json` (or `<name>.lock.json` for a single file), version 1:
target TFM/RID, sources, exact versions, nupkg SHA-512, compile/runtime/native asset lists and
dependency edges. Written atomically on install; never updated implicitly; the trust digest covers
its bytes.

**Trust placement.** Discovery parses the declaration, then the host computes
`sha256("lunate-extension-v1" | id | [package id@version] | source bytes | lock bytes)` and asks
before **restore** and **compile**. Analyzers and source generators are named in the prompt as
code that runs at build time. A cache hit re-checks the digest before loading.

**Isolation rules.** One collectible ALC per extension; `Lunate.Extensibility.Abstractions`,
`Lunate.Agent`, `Lunate.Ai`, `Microsoft.Extensions.AI.Abstractions` and `System.Text.Json` always
come from the host; every other dependency from the extension directory. Refuse packages whose
only delivery is MSBuild (`build*/` assets with no assemblies for the TFM); allow packages with
assemblies and record that their targets are ignored; select native assets by RID and copy them
next to the entry; key the cache by TFM, RID and host API version.

**Manifest shape.** Sidecar JSON beside the `.cs`, optional (inferred identity when absent),
same schema and parser as `extension.json`; packages keep the manifest inside the package.

**Cache layout.** Restored packages go to **NuGet's global packages folder** (`NUGET_PACKAGES`
when set, otherwise `~/.nuget/packages`): content-addressed, immutable, shared across extensions
and with the user's SDK cache, and not ours to prune by size. Per extension,
`~/.lunate/cache/extensions/<id>/<key>/` holds the compiled assembly and the lock file, with
`<key> = sha256(sourceHash | lockBytesHash | apiVersion | TFM | RID)[..16]` and a `current.json`
pointer. A successful compile keeps the **last two** builds and deletes older ones; uninstall
deletes the extension's outputs; `lunate cache prune` removes orphans.

**Phases.** (1) single-file discovery, sidecar, trust digest, `/extensions` states without
compilation; (2) restore + lock + compile + cache, managed assets only; (3) `/extensions reload`
through the command leases; (4) packaged `lunate install` on the same component; (5) native assets,
feed credentials and cache maintenance hardening.

## Proven by measurement vs expected

**Proven here:** the restore and compile timings; the SDK comparison on a warm SDK; the binary,
startup and RSS cost of the libraries; nuget.org, project and user config merging; basic-auth
private feeds and the poor 401 message; flat folder feeds; lock-file content and digest
sensitivity; trust-before-restore with zero network requests; source generators executing at
compile time; host-wins assembly binding for a conflicting `System.Text.Json`; RID native asset
selection and loading; MSBuild-only refusal and the targets-with-assemblies allowance; offline
cache hits and offline-miss messages; `file:line` compile errors; cache-hit loads without loading
Roslyn or NuGet; the agent loop writing a valid single file that the product ignores.

**Expected, not proven:** full NuGet conflict resolution and downgrade behaviour (`NuGet.Resolver`
is recommended but the probe used a nuspec walk); credential-provider plugins (no plugin
installed); private feeds beyond basic auth (bearer tokens, Azure Artifacts, client certificates);
Windows and Linux RID selection and native loading; package signature verification; multi-file single-file extensions (out of
scope; that is the packaged format); concurrent installs of the same id; cache eviction under
pressure; unload reliability for native libraries (a reload of SkiaSharp may not release the
dylib); long-running handler drain under real command load.

## Maintainer decisions (2026-10-24)

1. **Manifest** — header directives in the `.cs` (`#:extension`, `#:tool`, `#:hook`, `#:package`),
   parsed before compilation by the same scanner, covered by the trusted source digest. Neither an
   always-sidecar nor an inferred declaration; a sidecar stays optional. An undeclared registration
   is refused, a declared-but-missing one is warned.
2. **`#:package` parsing** — our own scanner, matching the .NET 10 directive syntax (including
   version ranges); `#:sdk`, `#:property` and `#:project` are refused by name. No SDK code in the
   host; the accepted grammar is pinned by tests.
3. **A shipped Abstractions copy** — accepted at install, always ignored at load (host wins, same
   for MEAI). Compatibility is decided by the Abstractions **version the extension references**: a
   reference above the host's `ExtensionApi` version is refused with *"requires Lunate ≥ x.y"*. The
   template and docs show `PrivateAssets="all"`, `ExcludeAssets="runtime"`; `/extensions` warns once
   when a copy was ignored.
4. **Cache size** — fixed now, not later: restored packages in NuGet's global packages folder
   (`NUGET_PACKAGES`), per-extension compiled assembly + lock only, keep the last two builds,
   delete on uninstall, `lunate cache prune` for orphans. No size-based pruning of the shared
   folder.

Also accepted: the in-process NuGet libraries (+15.8 MiB binary, +19 MB RSS after the UI is ready),
and trust before restore over source **plus lock bytes**, with the stale-digest-zero-feed-requests
measurement kept in the suite permanently.

The single-file path is estimated at **8–11 dev days**; the packaged format follows on the same
restore/lock/trust/cache component.
