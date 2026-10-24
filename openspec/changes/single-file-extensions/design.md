## Context

A packaged extension is a directory with a manifest and a prebuilt DLL. A single-file extension is one `.cs` file the host compiles for you. The spike measured both paths and the ADR addendum fixed the decisions; this design records how the four phases are cut and what is deliberately deferred.

## Decisions

**The declaration is in the file, not beside it.** A sidecar JSON would make "one file" false and, worse, put the declaration outside the bytes the user trusted. The header is inside the source digest, so the claim "this file provides these tools and restores these packages" is exactly what was approved. The scanner is ours — ~200 lines — because the SDK's file-based-app parser is internal, versioned with the SDK, and would drag SDK code into the host. `#:package` keeps the documented syntax so a file written for `dotnet run app.cs` reads the same here.

**Trust before restore.** Restore downloads, compile executes analyzers and generators. Asking after either would be asking about work already done on the user's machine. The digest is `sha256("lunate-extension-v1" | id | packages | source | lock)`, so editing the source, adding a package, or hand-editing the lock all invalidate it. The zero-feed-on-stale test is the enforcement, not a comment.

**Cache: the shared folder is NuGet's, the builds are ours.** Restored packages are content-addressed and immutable, so they belong in `~/.nuget/packages` where the user's SDK already caches them — not in a second copy under `~/.lunate`. What we own is the compiled assembly and the lock. Keying by source hash makes a build reproducible and makes a stale build impossible to confuse with a fresh one; keeping two covers the edit-compile-fail-fix loop without unbounded growth.

**Isolation is the existing extension story, unchanged.** One collectible context, host-owned contract assemblies, an API-version gate. What is new is only that a package can now drag in a copy of the Abstractions: accepted at install, ignored at load, warned once, decided by the referenced version rather than by DLL presence.

**Four phases, each a PR.** (1) discovery, scanner, trust, `/extensions` states — no compiler. (2) restore, lock, compile, cache. (3) reload through the lease path. (4) the loop end to end: the model writes a file with a package, the user trusts it, `/extensions reload` makes it live in the same session. Phase 1 alone is useful (a file is discoverable and refusable) and testable without NuGet.

## Risks

- **Analyzers are code execution at build time.** Named in the prompt; the digest covers the package list, so adding a package always re-asks.
- **Native assets.** The spike loaded SkiaSharp's `runtimes/osx/native` asset successfully; other RIDs are untested and phase 2 reports what it selected rather than assuming.
- **Signature verification** is not in scope: a package's author signature is not checked in this change, and the trust prompt says the digest covers the file and the packages, not a signature.
- **Credential-provider plugins** for private feeds are not wired; a feed needing one fails with a message naming the missing plugin.

## Deferred

Packaged install (`lunate install <id>`), size-based pruning of the shared packages folder, and unload of native libraries on reload (the ALC unloads managed code; a native library whose handle is still open stays mapped).
