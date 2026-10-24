## Why

Lunate can run C# extensions, but only as a built project: someone has to create a `.csproj`, reference the Abstractions, build it, and point the host at the DLL. That is a wall in front of the one thing the positioning promises — *"extend it in C#, from a single file to a full NuGet package"* — and it is the difference between an agent a .NET developer can shape in an afternoon and one that requires a solution on disk.

The spike (`docs/spikes/extension-restore.md`) proved the shape is affordable: one file, one header, in-process restore, a compile in ~2 s warm and ~1.5 s cold, three milliseconds to load a cached build, and no eager cost — the NuGet libraries are image size, not startup work.

## What Changes

- **A `.cs` file becomes an extension.** Discovery also reads `*.cs` under `.lunate/extensions/` and `~/.lunate/extensions/`; a header declares what the file provides, and the host compiles it on first use.
- **The declaration lives in the file**, parsed by the same scanner that reads `#:package`, before any compile, and covered by the trusted digest. `#:extension`, `#:tool`, `#:hook`, `#:package`. A sidebar JSON stays optional for single files; packaged extensions keep the existing manifest.
- **Trust comes before restore.** The digest covers the identity, the declared packages, the source bytes including the header, and the lock bytes. Approving code that is then restored and compiled is the point: analyzers and source generators are build-time code execution, and the prompt says so. A stale digest stops with **zero feed requests**.
- **Restore, lock, compile, cache.** NuGet client libraries in process, loaded only when a restore runs. Packages resolve into NuGet's global packages folder (shared, content-addressed, `NUGET_PACKAGES` respected); per extension we keep the compiled assembly and `extension.lock.json` and the last two builds, keyed by source hash.
- **Isolation unchanged in spirit.** One collectible load context per extension; the host's `Abstractions`, `Agent`, `Ai`, MEAI and `System.Text.Json` win over any copy a package brings; compatibility is decided by the Abstractions version the extension references, not by whether a DLL is present.
- **Lifecycle.** `/extensions` gains `not-built`, `restoring`, `failed (restore|compile|trust)`, `stale`; `/extensions reload` runs the existing lease path (trust → build first → cancel → notice → bounded drain → dispose → unload → load → `ToolsChanged`) so a running turn is never yanked out from under a tool call.

Packaged installation (`lunate install <id>`) stays out of scope: it reuses this restore, lock, trust and cache code and is its own change.
