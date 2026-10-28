# Roslyn public-API audit — does the flagship use only the public surface?

**Date:** 2026-10-28 · **Subject:** `src/Lunate.Roslyn` · **Question:** does the flagship use only the
public extension API? **Verdict: no — it uses none of it.** It is a host library composed by
`Lunate.Coding`, not an extension loaded through `Lunate.Extensibility`. Every "shortcut" below is a
composition point in the host or a missing primitive in the contract, not a compile-time reach into
another assembly's internals.

## Method

- Read every file under `src/Lunate.Roslyn` and every host call site that names it.
- Searched for `IExtensionFactory`, `IExtensionContext`, `ExtensionToolRegistration`,
  `IExtensionToolHandler`, `InternalsVisibleTo` and `using Lunate.Extensibility` across the project.
- Compared the result with the claims in `docs/guide.md`, `docs/architecture.md`,
  `docs/spec/extensibility.md` and `openspec/specs/agent-tools/spec.md`.
- Re-ran the layering rules (`tests/Lunate.Coding.Tests/LayeringChecker.cs`) to see which edge the
  repository explicitly permits.

The project has **no** compile-time internal access to another Lunate assembly: no project grants
`InternalsVisibleTo` to `Lunate.Roslyn`, and `Lunate.Roslyn` references only `Lunate.Agent`
([`Lunate.Roslyn.csproj:3`](../src/Lunate.Roslyn/Lunate.Roslyn.csproj#L3)). So the audit is about
process-level composition, not `internal` keywords.

## The two facts that decide the answer

1. `Lunate.Roslyn` registers **zero** contributions through the extension contract. `grep` for
   `IExtensionFactory|IExtensionContext|ExtensionToolRegistration|IExtensionToolHandler|
   using Lunate.Extensibility` over `src/Lunate.Roslyn` returns nothing. Its five tools implement
   `Lunate.Agent.ITool` directly
   ([`CsDiagnosticsTool.cs:13`](../src/Lunate.Roslyn/CsDiagnosticsTool.cs#L13) and its four siblings)
   and are constructed with a host-supplied `Func<ICSharpBackend>`.
2. The host composes them by hand. `HarnessFactory.AddCSharpTools` creates the five objects and adds
   them to the `ToolRegistry` ([`HarnessFactory.cs:152`](../src/Lunate.Coding/HarnessFactory.cs#L152)),
   the per-workspace cache is the internal `WorkspaceServices`
   ([`WorkspaceServices.cs:18`](../src/Lunate.Coding/WorkspaceServices.cs#L18)), and the win is
   recorded as an allowed host edge in the layering test
   ([`LayeringChecker.cs:55`](../tests/Lunate.Coding.Tests/LayeringChecker.cs#L55): *"The composition
   owns the per-workspace Roslyn services and composes the cs_\* tools"*).

`Lunate.Extensibility.Abstractions` appears nowhere in the project. The 348-line
`PublicAPI.Unshipped.txt` exists because the host binds to concrete Roslyn types
(`RoslynBackend`, `ICSharpBackend`, every result record), which is the opposite of the extension
boundary: an extension needs one public factory and nothing else.

## Inventory — every shortcut

`Today?` means: **exists** — the public API is there and the flagship does not use it; **workaround** —
the extension can reimplement the host facility at a cost; **missing** — no public primitive exists;
**cannot** — the public model forbids it, so a policy decision is needed first.

| # | Area | Site | What it touches | Public API it would need | Today? |
| --- | --- | --- | --- | --- | --- |
| 1 | Loading | [`Lunate.Roslyn.csproj:3`](../src/Lunate.Roslyn/Lunate.Roslyn.csproj#L3) | References `Lunate.Agent` directly; never `Lunate.Extensibility.Abstractions` | Project reference to the contract; compile against `IExtensionFactory`/`IExtensionContext` | exists |
| 2 | Loading | `src/Lunate.Roslyn/` (no factory) | No `IExtensionFactory` implementation anywhere; nothing for the loader to instantiate | `IExtensionFactory.Create(IExtensionContext)` + `IExtension` | exists |
| 3 | Loading | `src/Lunate.Roslyn/` (no manifest) | No `extension.json`; `apiVersion`, `tools`, `settingsSchema` never declared | `extension.json` parsed by `ExtensionManifest` | exists |
| 4 | Loading | [`ExtensionLoader.cs:101`](../src/Lunate.Extensibility/ExtensionLoader.cs#L101), [`ExtensionLoader.Load.cs:18`](../src/Lunate.Extensibility/ExtensionLoader.Load.cs#L18) | Discovery scans `~/.lunate/extensions` and `.lunate/extensions` only; `Load` requires a discovered id and a directory inside a scope | A first-party/built-in registration hook on `ExtensionHostOptions` or `ExtensionLoader` | **missing** |
| 5 | Composition | [`HarnessFactory.cs:152`](../src/Lunate.Coding/HarnessFactory.cs#L152) | Host constructs all five tools and calls `ToolRegistry.Add` | `IExtensionContext.RegisterTool(ExtensionToolRegistration)` | exists |
| 6 | Tool shape | [`CsDiagnosticsTool.cs:13`](../src/Lunate.Roslyn/CsDiagnosticsTool.cs#L13) (×5) | Implements `ITool` with a `Func<ICSharpBackend>` constructor; handler instance is created by the host | `IExtensionToolHandler` + `ExtensionToolRegistration`; per-call workspace from `ToolContext` | exists |
| 7 | Workspace lifecycle | [`WorkspaceServices.cs:18`](../src/Lunate.Coding/WorkspaceServices.cs#L18), [`:32`](../src/Lunate.Coding/WorkspaceServices.cs#L32), [`:280`](../src/Lunate.Coding/WorkspaceServices.cs#L280) | Internal per-root registry with LRU cap, pinning, disposal; host calls `new RoslynBackend(root)` | A public per-workspace service facility (keyed by `WorkspaceInfo`, capped, lazily started) | **missing** |
| 8 | Workspace lifecycle | [`InteractiveSession.cs:425`](../src/Lunate.Coding/InteractiveSession.cs#L425), [`PrintMode.cs:265`](../src/Lunate.Coding/PrintMode.cs#L265), [`AcpMode.cs:39`](../src/Lunate.Coding/AcpMode.cs#L39) | Three frontends construct and dispose the registry; an extension would have to own the cache in a background service | `IBackgroundService` start/stop (exists) + per-workspace scoping from #7 | workaround |
| 9 | Workspace lifecycle | [`IExtensionContext.cs:19`](../src/Lunate.Extensibility.Abstractions/IExtensionContext.cs#L19), [`Services.cs:8`](../src/Lunate.Extensibility.Abstractions/Services.cs#L8) | No workspace-scoped hook: `IBackgroundService` is session-scoped, `ToolContext` is per call | Per-workspace service creation/eviction callback | **missing** |
| 10 | Availability | [`HarnessFactory.cs:36`](../src/Lunate.Coding/HarnessFactory.cs#L36), [`:127`](../src/Lunate.Coding/HarnessFactory.cs#L127) | Core decides whether the tools exist (`.sln`/`.slnx`/`.csproj` at the root); extension tools are always registered | Conditional/dynamic tool exposure per workspace (e.g. `ExtensionToolRegistration.IsAvailable`) | **missing** |
| 11 | Availability | [`HarnessFactory.cs:89`](../src/Lunate.Coding/HarnessFactory.cs#L89) | Child runs get C# tools rebound to the child root; extension tools are inherited as-is | `ToolContext.WorkingDirectory` rebinding (exists) or #7 | workaround |
| 12 | Naming | [`CsDiagnosticsTool.cs:36`](../src/Lunate.Roslyn/CsDiagnosticsTool.cs#L36) vs [`ExtensionToolAdapter.cs:29`](../src/Lunate.Extensibility/ExtensionToolAdapter.cs#L29) | Model-facing names are `cs_*`; extension tools are forced to `<id>__<local>` | An un-prefixed first-party name exception — or accept `roslyn__cs_*` and rename | **cannot** (policy) |
| 13 | Assemblies | [`HostOwnedAssemblies.cs:28`](../src/Lunate.Extensibility/HostOwnedAssemblies.cs#L28), [`ExtensionLoadContext.cs:24`](../src/Lunate.Extensibility/ExtensionLoadContext.cs#L24) | Only four assemblies are shared; a loaded extension would bring its own Roslyn/MSBuild into a collectible ALC | A first-party shared-assembly policy, or built-ins in the default ALC | **missing** |
| 14 | Assemblies | [`MsBuildBootstrap.cs:15`](../src/Lunate.Roslyn/MsBuildBootstrap.cs#L15), [`:28`](../src/Lunate.Roslyn/MsBuildBootstrap.cs#L28) | `MSBuildLocator.RegisterDefaults()` is process-wide; a second registration is an error | A core "MSBuild is already bootstrapped" seam, or a documented first-registration contract | **missing** |
| 15 | Settings | [`SettingsStore.cs:68`](../src/Lunate.Coding/SettingsStore.cs#L68), [`:564`](../src/Lunate.Coding/SettingsStore.cs#L564) | `roslyn.maxLoadedWorkspaces` is a core settings section parsed by the host | `IExtensionSettings` + manifest `settingsSchema` | exists |
| 16 | File changes | [`WorkspaceServices.FileChanges.cs:31`](../src/Lunate.Coding/WorkspaceServices.FileChanges.cs#L31), [`:65`](../src/Lunate.Coding/WorkspaceServices.FileChanges.cs#L65) | Host subscribes to `core/file-bus`, filters by extension and routes by workspace id | `IExtensionContext.SubscribeFileChanged` + `IFileChangedHandler` (payload carries `WorkspaceId`) | exists |
| 17 | Results | [`CsDiagnosticsTool.cs:79`](../src/Lunate.Roslyn/CsDiagnosticsTool.cs#L79), [`:96`](../src/Lunate.Roslyn/CsDiagnosticsTool.cs#L96) | Rich `ToolResult.Details` objects are invisible to extension renderers | `ToolResultView` carrying `StructuredContent` (JSON), not only `ResultText` | **missing** |
| 18 | Presentation | [`CsToolMeta.cs:10`](../src/Lunate.Tui/ToolBlocks/CsToolMeta.cs#L10), [`ToolBlockMeta.cs:55`](../src/Lunate.Tui/ToolBlocks/ToolBlockMeta.cs#L55) | Core TUI parses `cs_*` names and result text to print "1 error 0 warnings" in the block header | `IToolResultRenderer` (exists, but renders a separate block — see #17) or a generic meta line | **cannot** (renderer cannot feed the block header) |
| 19 | Presentation | [`ToolArgsSummary.cs:125`](../src/Lunate.Tui/ToolBlocks/ToolArgsSummary.cs#L125) | Preferred argument keys (`symbol`, `name`, `file`) are selected by the `cs_` prefix | A per-tool argument-hint declaration on the tool contract, or fall back to the first string | exists (fallback) |
| 20 | Presentation | [`WelcomeHeaderRenderer.cs:24`](../src/Lunate.Tui/WelcomeHeaderRenderer.cs#L24), [`InteractiveSession.Header.cs:60`](../src/Lunate.Coding/InteractiveSession.Header.cs#L60) | Core advertises `C# workspace · Roslyn` from a core C#-workspace scan | Extension-contributed header state/capability; `IStatusWidget` is footer-only | **missing** |
| 21 | Tests | [`Lunate.Roslyn.csproj:25`](../src/Lunate.Roslyn/Lunate.Roslyn.csproj#L25), [`:28`](../src/Lunate.Roslyn/Lunate.Roslyn.csproj#L28) | `InternalsVisibleTo` lets `Lunate.Coding.Tests` read `LoadCount` and the bootstrap seam | Public observability (results/events/log) instead of an internal counter | exists (redesign the test) |
| 22 | Public surface | [`PublicAPI.Unshipped.txt`](../src/Lunate.Roslyn/PublicAPI.Unshipped.txt) | 348 tracked lines / 282 public API entries because the host binds to concrete types | One public factory; everything else internal to the extension | exists (after #1–#5) |

Not a shortcut: the tools use `Lunate.Agent.ITool`, `ToolContext` and `ToolResult`
([`ITool.cs:11`](../src/Lunate.Agent/ITool.cs#L11)) — the same public tool model extension handlers
receive. That part is already the common contract.

## What the public surface already covers

These need no new API; the flagship simply does not use them:

- **Registration:** `IExtensionFactory`, `IExtensionContext.RegisterTool`, `ExtensionToolRegistration`,
  `IExtensionToolHandler`.
- **Workspace identity:** `ToolContext.WorkingDirectory` per call, and `core/workspace`
  (`WorkspaceInfo`) for the session root.
- **File changes:** `IExtensionContext.SubscribeFileChanged`, `IFileChangedHandler`,
  `FileChangedPayload.WorkspaceId`.
- **Settings:** `IExtensionSettings` plus the manifest `settingsSchema`.
- **Lifecycle:** `IBackgroundService` (session start/stop), and the adapter's per-extension
  cancellation on unload.
- **Presentation:** `IToolResultRenderer`, `IStatusWidget`.

## Recommended order

The goal is not "port the code" but "make the claim true". Each step is an architecture change and
needs maintainer sign-off before implementation.

1. **A built-in extension registration hook (P0).** Add the smallest primitive that lets the host
   hand `ExtensionLoader` an in-box `IExtensionFactory` (id, manifest, no discovery, no trust
   prompt) and run it through the same context, registries, adapters and unload path. Without this,
   `Lunate.Roslyn` cannot be an extension at all; with it, items #1–#5 disappear and the guide's
   claim becomes mechanically true. Run built-ins in the default load context so #13 is moot for
   them; keep the four-assembly sharing rule for third-party load contexts.
2. **A per-workspace service facility (P0).** Generalise `WorkspaceServices` into a contract the
   context exposes (`GetOrCreateWorkspace<T>(WorkspaceInfo, factory)` with lazy start, LRU cap and
   pinning). ADR-0017 and `docs/spec/extensibility.md` already promise per-workspace services keyed
   by `WorktreeRoot`; today only the host can have them. This also unblocks the LSP sample.
3. **Conditional tool availability (P1).** Decide how a tool says "not in this workspace". A static
   registration cannot express the `.sln`/`.csproj` gate without putting the file scan in every
   call. Options: a registration predicate evaluated per workspace, or a workspace-capability query
   the extension can read at composition. Without it, a TypeScript repo sees five dead C# tools.
4. **The name decision (P1).** Either accept `roslyn__cs_diagnostics` (consistent, collision-free,
   but changes prompts, TUI tests and docs) or add a narrowly scoped first-party exception. Prefer
   accepting the prefix: the extension namespace is the point of the mechanism, and a first-party
   exception weakens the contract for everyone else.
5. **Renderer parity (P1).** Put `StructuredContent` (JSON) on `ToolResultView`, then move the
   `cs_*` summary out of `Lunate.Tui` into a renderer registered by the extension. That removes
   rows #18–#19 and makes the boundary JSON-truthful. If the block header is the desired home,
   add a renderer contribution for it rather than string-matching a tool name in core.
6. **Settings and lifecycle ownership (P2).** Move `roslyn.maxLoadedWorkspaces` to the extension's
   `settingsSchema` (with a compatibility read from `settings.json`), let the extension own
   `IBackgroundService` start/stop, and delete `RoslynSettings` from core. The host keeps no
   Roslyn-shaped setting.
7. **Presentation and test seams (P2).** Either contribute the header line through a capability
   (#20) or document it as a core workspace fact. Replace the `InternalsVisibleTo` test hooks with
   assertions on public results, events and logs (#21), and shrink the extension's public surface
   to its factory (#22).

Items 1–2 are the gate: until they land, the honest statement is *"the C# tools are a core
capability, and the extension surface is proven by the sample fitness suite"* — not *"the flagship
is built only on the public extension API"*.

## Documents that disagree with the code

- `docs/guide.md:19-21` — "the flagship extension, built only on the public extension API".
- `docs/guide.md:64`, `:68` — "Roslyn as a built-in extension"; "`Lunate.Roslyn` is loaded through
  the extension loader".
- `docs/guide.md:467` — "ships as a built-in extension and uses the same mechanism".
- `docs/architecture.md:14-15` — the diagram edge says `Lunate.Roslyn` "implements the extension
  contract" and points it at `Lunate.Agent`; it implements the tool contract, not the extension
  contract.
- `openspec/specs/agent-tools/spec.md:190` and `docs/spec/extensibility.md:86`, `:100` describe the
  Roslyn backend as a core per-workspace service and the `cs_*` tools as optional core tools. That is
  what the code does.
- `tests/Lunate.Coding.Tests/LayeringChecker.cs:55` whitelists `Lunate.Coding -> Lunate.Roslyn` as
  host composition.

Per `AGENTS.md`, the spec is the source of truth: until a change moves Roslyn onto the contract,
the guide and the architecture diagram are the documents to fix, not the code.

## What this audit did not do

No source code, spec or guide text changed. Moving the flagship onto the contract is an architecture
decision (ADR-0017, the extension surface, tool naming, load context), so it needs an OpenSpec
change and maintainer sign-off before implementation. This document is the evidence for that
decision, not the decision.

## Re-running the audit

```bash
grep -rn "IExtensionFactory\|IExtensionContext\|ExtensionToolRegistration\|IExtensionToolHandler" src/Lunate.Roslyn
grep -rn "using Lunate.Extensibility" src/Lunate.Roslyn
grep -rn "InternalsVisibleTo" src/*/*.csproj | grep -i roslyn
grep -rn "cs_\|Lunate.Roslyn" src/Lunate.Tui src/Lunate.Coding --include="*.cs"
grep -c '^Lunate.Roslyn' src/Lunate.Roslyn/PublicAPI.Unshipped.txt
```
