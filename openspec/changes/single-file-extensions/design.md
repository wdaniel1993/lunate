## Context

A packaged extension is a directory with a manifest and a prebuilt DLL. A single-file extension is one `.cs` file the host compiles for you. The spike measured both paths and the ADR addendum fixed the decisions; this design records how the four phases are cut and what is deliberately deferred.

## Decisions

**The declaration is in the file, not beside it.** A sidecar JSON would make "one file" false and, worse, put the declaration outside the bytes the user trusted. The header is inside the source digest, so the claim "this file provides these tools and restores these packages" is exactly what was approved. The scanner is ours — ~200 lines — because the SDK's file-based-app parser is internal, versioned with the SDK, and would drag SDK code into the host. `#:package` keeps the documented syntax so a file written for `dotnet run app.cs` reads the same here.

**Trust before restore, and the lock is bound by the restore.** Restore downloads but executes nothing, so the honest order is: the user approves the file, its declared packages as written and its source; the first successful restore writes the lock and its graph is bound into the trust record without a second question; compile only ever runs against that bound lock. From then on the digest is `sha256("lunate-extension-v2" | id | packages | source | the lock's graph)`, so editing the source, adding a package or hand-editing the resolution all make the file stale — while a .NET patch, which only moves the lock's toolset half, does not. The zero-feed-on-stale test is the enforcement, not a comment.

**Build-time code is opt-in.** Compiling against a package's compile-time assemblies does not require its analyzers or generators. They execute at build time, and most extensions never need them, so they are off unless the header says `#:generators on` — which is in the digest, and turns on the prompt line that says build-time code from those packages will run. A package that ships generators while the file did not opt in is named in `/extensions` as `generators skipped: <package>`. This also removes the spike's 4.1 s analyzer/generator cost from the common compile.

**The header covers every declaration kind.** The declaration rule refuses what the header does not declare, so a header limited to tools and hooks would make single files second-class: no commands, no services, no model providers. The header therefore mirrors the manifest field for field (`tools`, `commands`, `hooks`, `services`, `capabilities`, `modelProviders`), and a test fails when the manifest grows a field that has no directive. Only the leading block is scanned — directives, comments and blank lines before the first C# token, the same rule the SDK uses — and a `#:` line after code is an error naming its line rather than a silent no-op.

**Reference assemblies come from NuGet.** A published self-contained binary has no SDK and no reference pack on disk, which is the same reason restore runs in process. The matching `Microsoft.NETCore.App.Ref` (runtime version, RID) is restored through the same engine into the global packages folder on first compile and reused. Shipping it inside the binary is the alternative; phase 2 reports its size rather than guessing, and a test compiles with the published binary, an empty `HOME` and no `dotnet` on `PATH`.

**The model never approves trust.** The phase-4 loop has the model write the file; trust stays the user's. No tool, command or protocol path records a decision, suppresses a prompt, or loads a stale digest — a model-initiated reload of a changed file ends at the prompt and waits.

**Cache: the shared folder is NuGet's, the builds are ours.** Restored packages are content-addressed and immutable, so they belong in `~/.nuget/packages` where the user's SDK already caches them — not in a second copy under `~/.lunate`. What we own is the compiled assembly and the lock. Keying by the source, the lock's two halves, the API version, TFM and RID makes a build reproducible, makes a stale build impossible to confuse with a fresh one, and makes a toolset change build anew instead of reusing an assembly compiled against another reference pack; keeping two covers the edit-compile-fail-fix loop without unbounded growth.

**The prompt cannot know the transitive set.** Before a restore, nothing has resolved the graph, so the prompt names the declared packages "and their dependencies" rather than pretending to list them; after the restore, `/extensions` shows the resolved graph from the lock.

**Isolation is the existing extension story, unchanged.** One collectible context, host-owned contract assemblies, an API-version gate. What is new is only that a package can now drag in a copy of the Abstractions: accepted at install, ignored at load, warned once, decided by the referenced version rather than by DLL presence.

**Four phases, each a PR.** (1) discovery, scanner, trust, `/extensions` states — no compiler. (2) restore, lock, compile, cache. (3) reload through the lease path. (4) the loop end to end: the model writes a file with a package, the user trusts it, `/extensions reload` makes it live in the same session. Phase 1 alone is useful (a file is discoverable and refusable) and testable without NuGet.

## Phase 2a implementation notes

Phase 2a is restore, lock, trust binding and the reference pack: no compile, no build cache and no
generators (those are 2b). Three decisions had to be made concretely, and two of them correct what
the ADR addendum said — the correction is the decision.

**The lock lives in the cache entry, for both scopes.** The addendum said `<name>.lock.json` next to
the extension. That contradicts the promise the file format exists for: a generated sidecar in the
user's extensions directory makes "one file" false again, and the extensions directory is the one
place the user edits while the host watches. The lock is written to
`~/.lunate/cache/extensions/<id>/extension.lock.json` for project and user extensions alike — keyed
by id only, because the *build* key already includes the lock hash and the lock cannot live inside
the directory it names. The consequence belongs in the authoring docs: a lock is produced by a
restore, on the machine that runs it. Nothing is committed, copied or shared; a teammate's first run
restores and asks there. What makes two machines agree is the resolution algorithm — the lowest
version that satisfies every range does not move when new versions are published — not a checked-in
file.

**The lock is two halves with two jobs.** The **graph** — sources, package ids, exact versions,
SHA-512, resolved assets and dependency edges — is what a trust decision covers: a change to it is a
change to what the user approved, so it re-prompts. The **toolset** — TFM, RID, exact runtime, the
reference pack's version and its nupkg hash (when the packages folder still holds it) — is
machine-local: a .NET patch moves it without moving anything the user decided. It is therefore not
part of the trust digest, it is integrity-checked on restore (a block edited by hand is refused, not
believed), and it is part of the build cache key through the whole-lock hash. A toolset difference
triggers a **silent rebuild** and is reported as a rebuild reason; it is never a trust question.
This reverses the note the first draft carried — "a runtime change asks again, and that is the price
of a deterministic lock" — because the price bought nothing: the resolution did not change, so there
was no decision to take.

**A lock is the file's lock only when it says so.** Every lock records the source-level digest it
was resolved from. A lock whose digest is not the current file's is ignored for the trust digest
(it belongs to an earlier version of the file) and is reported as a change by the next restore.
This is what keeps a re-trusted, edited file from staling in a loop: the approval covers the source,
the restore writes the new lock, and the bind moves the record to the graph-inclusive digest with no
second question. A graph edited *without* touching the source keeps its source digest but changes
the graph bytes, so the file is stale and the user is asked; approving adopts the edited resolution
without re-resolving, because that is exactly the resolution they approved.

**Two assemblies, one seam.** `Lunate.Extensibility` owns the lock, the digest, the trust record
and the restore *policy*; `Lunate.Extensibility.Restore` owns the NuGet client libraries and is the
only project that references them. `Lunate.Coding` wires the engine as a factory
(`SingleFileRestoreFactory.Engine`), so composing a session constructs nothing and loads no
`NuGet.*` assembly; `SingleFileLazyRestoreTests` composes an all-built session in a child process
and fails if one appears. Restore is not *triggered* in this phase: its callers are the compile step
(2b) and `/extensions reload` (phase 3), which is why startup still reads declarations only.

**Resolution rule.** The walker reproduces NuGet's PackageReference answer where NuGet answers
without a warning: a transitively required package takes the lowest version that satisfies every
range asked of it — the highest of the range minimums when they overlap — a directly declared
package stays inside its own declaration and is not dragged out of it by an edge, and a floating
version resolves to the highest published match at lock time. The requirement set is rebuilt from
the current choices on each pass, so an edge that disappears with a version change does not linger,
and the answer is deterministic: the same declarations always serialize to the same lock.

It refuses exactly where NuGet warns or fails — a downgrade (NU1605), a version outside a dependency
range (NU1608), a conflict no single version satisfies (NU1107) — naming the package, the ranges and
every asker, and ending with the fix: `add #:package <id>@<version> satisfying all ranges`. The one
place the code differs is a cousin conflict NuGet only warns about: we refuse rather than choose a
version nobody asked for. `NuGetConformanceTests` holds the walker to `dotnet restore` on real
graphs, so this paragraph is checked rather than asserted.

**MSBL001.** `NuGet.Frameworks` is a runtime asset of the restore engine and MSBuild ships its own
copy, so `Microsoft.Build.Locator`'s build check fires in every project that references the app. The
check cannot express the rule that matters ("no `Microsoft.Build.*` assembly is copied beside the
app"), so it is disabled in `Directory.Build.props` and that rule is asserted by
`AssemblyLayoutTests` instead. The overlap is real but bounded: MSBuild's copy is loaded only by the
Roslyn workspace path, Lunate's only when a restore runs.

## Phase 2b implementation notes

Phase 2b is the compile, the build cache and the published-binary proof: no generators, no
measurements and no docs pass (those stay 2.13–2.16). Four decisions had to be made concretely.

**The trigger is `lunate extensions build <id|path> [--json]`.** Listing extensions must not compile
— `/extensions` is a state view, and its rows would start costing seconds — and startup must stay
lazy, so a build needs a surface of its own. The CLI subcommand is the smallest honest one: it
discovers declarations only, then calls the same `SingleFileExtensionBuilder` that `/extensions
reload` calls in phase 3, so the order (trust, then the lock that answers for those bytes, then the
cache, then the compiler) exists once. It never prompts: trust is the user's decision, so a file
that is not trusted for its bytes reports `failed (trust)` and says to approve it in an interactive
session. `--json` prints the build key and the assembly path on success, and the first diagnostic as
`{file, line, column, id, message}` on a compile failure.

**The compile-time contract travels inside the binary.** A published self-contained single-file app
bundles its managed assemblies: `Assembly.Location` is empty and there is no `.dll` beside the
executable to point a `MetadataReference` at — yet an extension has to compile against
`Lunate.Extensibility.Abstractions` and the host assemblies its signatures use. `Lunate.Extensibility.Compile`
therefore embeds the four shared contract assemblies (`Lunate.Extensibility.Abstractions`,
`Lunate.Agent`, `Lunate.Ai`, `Microsoft.Extensions.AI.Abstractions`) from the build's own resolved
references and hands the compiler those images. They are the same assemblies the host loads, so
type identity survives the compile/load boundary; a fallback reads them from `Assembly.Location` or
beside the host for a build that skipped the embedding target. This is also what makes the phase-3
isolation rule real: a package that ships its own copy of a contract cannot displace the host's
compile reference, because the host's is added first and the duplicate is dropped by simple name.

**Determinism is a property of the emit, not a hope.** The compile uses `deterministic: true` and
emits no PDB, so no source path, timestamp or machine identity can reach the image: the same source,
lock and toolset produce byte-identical output, and a test compiles the same bytes from two
directories to prove it. That is what lets the build key be a content address instead of a
timestamp.

**The cache is `builds/<key>/` plus one pointer.** The key is `sha256(sourceHash | lockHash |
apiVersion | TFM | RID)`, fields NUL-separated, rendered as lowercase hex and truncated to its first
16 characters — the manifest inside the build repeats the untruncated hashes, so the truncation loses
nothing a reader needs. `lockHash` is the SHA-256 of the whole lock file, so a graph change and a
toolset change each move the key. Each build directory holds `<id>.dll` and `build.json` — the key's
inputs and, per package, the runtime and native assets a load resolves out of the packages folder, so
phase 3 never has to pair a build with a lock that has since been rewritten. `builds.json` names the
current build and the surviving builds, newest first; retention and the pointer are one document, and
the order is written rather than inferred from mtimes, so "the last two builds" does not depend on a
clock. A failed compile writes nothing at all — no directory, no pointer, no trim — which is what
keeps the previous build the one the entry points at.

**Two things the phase-2a code had to be read carefully for.** A never-restored file's trust digest is
the graph-bound layout with an empty graph segment (`ComputeUnrestored` names it), not the older
source-only tag, so anything that seeds a trust record for a test or a script has to use that value;
the source-only digest stays the lock's `SourceDigest` and the build key's `sourceHash`. And Roslyn
5.9 rejects a `#:` line with `CS9298` unless the file-based-program feature is on, so the parse
options set it: the header is the host's declaration, and the compiler only has to stop refusing to
read the rest of the file.

## Phase 2c implementation notes

Phase 2c makes the generators opt-in real, wires the omission into the views, records the phase's
measurements and finishes the authoring page (tasks 2.13–2.16). The behaviour the design already
promised did not need a new decision; what it needed was the mechanism, and the mechanism has two
consequences worth writing down.

**The opt-in is a decision because the header is hashed, not because a flag is stored.** `#:generators on`
is a header line, and the header is inside the source bytes the digest covers — so the two files differ
in the digest for both layouts that exist (`lunate-extension-v1` for a lock's `SourceDigest`,
`lunate-extension-v2` for trust), toggling the line stales a trusted file exactly like editing code
does, and no separate field can drift out of sync with the bytes the user approved. Nothing was added
to the lock or the digest for it: the lock already records, per package, the analyzer assets and
whether the package ships build-time code, which is what makes the omission reportable at all.

**Generators are host code execution, so the difference is where the prompt line lives.** With the
directive, the compile opens each locked package's `analyzers/` assemblies as
`AnalyzerFileReference`s, runs their generators through `CSharpGeneratorDriver` (the emitted trees are
compiled in), and runs their analyzers through `Compilation.WithAnalyzers`; an analyzer's error ends
the compile the way it would in a project build, reported as the first diagnostic by position like
every other compile failure. Without it nothing under `analyzers/` is opened — the test asserts the
generator assembly is absent from the process afterwards, which is stronger than "the build worked".
The loader is `Assembly.LoadFrom` on purpose: the packages folder is not a probing path, and a
generator's dependency on `Microsoft.CodeAnalysis` has to bind to the Roslyn already in the process,
because a second copy would hand the driver objects it cannot recognise. That is also the honest
statement of the risk — a generator runs arbitrary code in the host — and it is why the prompt's
build-time line is conditioned on the same directive rather than being boilerplate.

**The omission is a row, not a log line.** `SingleFileExtension.GeneratorsSkipped` reads the lock
(after a restore, before a build) and names the packages whose build-time code the compile will leave
out; `/extensions` appends `, generators skipped: <package>` after the resolved graph, and both
`--json` surfaces carry `generatorsSkipped`. Before a restore it is empty: no lock means no resolved
package, and inventing a name from the declarations would guess at which package ships generators.
The hint disappears the moment the file opts in, which is what makes the toggle legible in both
directions.

### Measurements (task 2.15, this machine: macOS 26.3.1, osx-arm64, .NET 10.0.13)

All sizes are the OS's own, taken with `du -sk` and `stat`; numbers in prose carry their unit.

| What | Measured |
| --- | --- |
| One-package extension's cache entry (`builds/<key>/` + `extension.lock.json` + `builds.json`) | 13,525 bytes logical, 24 KiB allocated — assembly 2,560 B, lock 10,342 B, pointer 78 B, manifest 545 B |
| `Microsoft.NETCore.App.Ref` in the global packages folder (`10.0.5`, 359 files) | 51,000,217 bytes, 48.6 MiB (50,476 KiB allocated) — `ref/` 37,516 KiB, `analyzers/` 5,644 KiB, the nupkg 7,096 KiB |
| Published `osx-arm64` single-file binary with the four embedded contract assemblies | 364,267,911 bytes (347.4 MiB) |
| The same binary without them (`-p:EmbedCompileContracts=false`) | 362,860,263 bytes (346.0 MiB) |
| Difference the embedding costs | 1,407,648 bytes (1.34 MiB; the four assemblies themselves sum to 1,395,000 bytes, the rest is resource headers) |
| Startup median after this phase (`scripts/verify.sh`'s budget step) | 60 ms (budget 150 ms) |

The cache entry is dominated by the lock (76% of it), not by the assembly: a one-package extension
costs about 2.5 KiB of compiled code and 10 KiB of resolution. The reference pack is the real cost of
the feature — 49 MiB in the user's NuGet cache, restored once and shared with the SDK — and the
embedded contracts cost 1.34 MiB in the binary, which is why they are embedded rather than restored:
a published single-file binary has no assembly on disk to point a `MetadataReference` at.

### The both-orders MSBL001 proof

`MsblBindingOrderTests` runs the two acts the design named — a single-file build (which loads
`NuGet.Frameworks` through the restore stack) and a real C# workspace load through `Lunate.Roslyn`'s
loader — in both orders, each order in its own child process because a loaded assembly cannot be
unloaded. It is hermetic on all three legs: a temporary `HOME` and `NUGET_PACKAGES`, a local folder
feed seeded from the machine's reference pack, and a package-free workspace restored against a
cleared source list. Both halves are asserted by using them (the extension's own type is invoked, a
symbol in the workspace is found through the backend), and both are re-asserted after the second act,
so a conflict that breaks either side fails the test in both orders.

No `AssemblyLoadContext` isolation was needed, and the reason is worth recording because it corrects
the assumption behind MSBL001: after both orders, the process holds exactly one `NuGet.Frameworks`
(7.9.0.0, the app's own) and `Microsoft.Build.Framework` 15.1.0.0 from the located SDK. MSBuild's
design-time build runs in the Roslyn build host process — the out-of-process host whose DLLs the
published binary must keep loose (ADR-0006) — so MSBuild's copy of `NuGet.Frameworks` never enters the
host's binding context. The overlap that MSBL001 reacts to is therefore real in a project that
*references* the restore engine, and bounded at run time to `Microsoft.Build.Framework`, which does
not reference `NuGet.Frameworks`. The behavioural test stays the guard: if a future Roslyn loads
MSBuild in process, this test is where it shows up.

## Risks

- **Analyzers and generators are code execution at build time.** Off unless `#:generators on`; the opt-in is in the digest, so toggling it re-asks, and the prompt only mentions build-time code when it applies.
- **Native assets.** The spike loaded SkiaSharp's `runtimes/osx/native` asset successfully; other RIDs are untested and phase 2 reports what it selected rather than assuming.
- **Signature verification** is not in scope: a package's author signature is not checked in this change, and the trust prompt says the digest covers the file and the packages, not a signature.
- **Credential-provider plugins** for private feeds are not wired; a feed needing one fails with a message naming the missing plugin.

**A Lunate update rebuilds on first use.** The cache key includes the API version, TFM, RID and the lock's two halves, so a new host build always produces a new key. That is deliberate — an assembly compiled against one contract must not be reused by another — and the docs say it plainly so the first `/extensions` after an update is not mistaken for a bug.

## Deferred

Packaged install (`lunate install <id>`), signature verification, credential-provider plugins for private feeds (Azure Artifacts and friends — filed as its own backlog card), size-based pruning of the shared packages folder, and unload of native libraries on reload (the ALC unloads managed code; a native library whose handle is still open stays mapped).
