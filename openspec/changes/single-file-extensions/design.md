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

## Risks

- **Analyzers and generators are code execution at build time.** Off unless `#:generators on`; the opt-in is in the digest, so toggling it re-asks, and the prompt only mentions build-time code when it applies.
- **Native assets.** The spike loaded SkiaSharp's `runtimes/osx/native` asset successfully; other RIDs are untested and phase 2 reports what it selected rather than assuming.
- **Signature verification** is not in scope: a package's author signature is not checked in this change, and the trust prompt says the digest covers the file and the packages, not a signature.
- **Credential-provider plugins** for private feeds are not wired; a feed needing one fails with a message naming the missing plugin.

**A Lunate update rebuilds on first use.** The cache key includes the API version, TFM, RID and the lock's two halves, so a new host build always produces a new key. That is deliberate — an assembly compiled against one contract must not be reused by another — and the docs say it plainly so the first `/extensions` after an update is not mistaken for a bug.

## Deferred

Packaged install (`lunate install <id>`), signature verification, credential-provider plugins for private feeds (Azure Artifacts and friends — filed as its own backlog card), size-based pruning of the shared packages folder, and unload of native libraries on reload (the ALC unloads managed code; a native library whose handle is still open stays mapped).
