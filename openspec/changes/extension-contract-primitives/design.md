# Design — extension-contract primitives

- Status: proposed — 2026-10-29
- Change: `extension-contract-primitives` (#292, tasks.md 4.1 of the archived `extension-contract` change)
- Implements: openspec/specs/extensions/spec.md requirements *Built-in extension provenance* (:1496),
  *Per-workspace extension services* (:1556), *Conditional tool availability* (:1616),
  *Stable tool aliases* (:1741), plus the `/extensions` view delta in this change's specs.
- Accepted decisions: ADR-0017 addendum (2026-10-29), archived design §3–§6.
- Diagrams: C4-inspired, ASCII (lightweight rigor); unattended run, purpose = implementation of the
  accepted contract, so every open question below is resolved against the accepted design and code.

## 1. Resolved decisions (the grill-me branches)

| Branch | Resolution |
| --- | --- |
| API version | `ExtensionApi.Current` → `"1.12.0"` (semver-minor); contract assembly version, `ExtensionApiTests` pin and `PublicAPI.Unshipped.txt` follow. Three docs still naming 1.10.0 (`docs/extensions/manifest.md`, `lifecycle-and-trust.md`, `troubleshooting.md`) are refreshed (claims name what exists now). |
| Rule types + evaluator home | `Lunate.Extensibility.Abstractions`. It already references `Lunate.Agent` for `ToolContext` types, so manifest parsing (Abstractions), the loader (Extensibility) and the composition (Coding) all see one definition. `Lunate.Protocols` gets nothing yet (MCP-roslyn adoption rides with #293). |
| Manifest JSON names | camelCase keys like the existing ones: `builtin`, `workspaceServices`, `toolActivation`, `toolAliases`. The C# property is `BuiltIn` (default `false`). |
| Built-in descriptor | `ExtensionScope.Builtin` added; `ExtensionDescriptor.Directory` is null for built-ins; `ExtensionHostOptions.BuiltIns` is a new list of a `BuiltInExtension` record (`Manifest`, `Func<IExtensionFactory>`) in composition order. `ExtensionTestHostOptions` gains the same seam for tests. |
| Core-name reservation | Mirrors `ExtensionHostOptions.ReservedCommands`: a new `ReservedToolNames` list supplied by the composition with the core tool names (read/write/edit/bash/grep/find/ls). Parse-time checks are shape only; core-name collisions refuse the load (`extension '<id>': alias '<a>' was refused: it is a core tool name.`), because only the composition knows the set. |
| Workspace-service registry | New file in `Lunate.Extensibility` (`WorkspaceServiceRegistry.cs`) generalizing the documented lifecycle of `src/Lunate.Coding/WorkspaceServices.cs` (per root, cap, pin, LRU eviction of unpinned entries, all-pinned wait in cancellation-aware slices, dispose outside the lock, `ObjectDisposedException` after shutdown). The core `WorkspaceServices` stays until #293 migrates Roslyn; deduping is then, not now. |
| Alias application point | The extension's factory registers tools by local name only, so the alias must come from the manifest: the (internal) `ExtensionContext` receives the manifest's alias and activation tables at creation and hands them to the tool registry, which computes the model-facing name as `alias ?? <id>__<local>`. `ExtensionToolNaming.ModelFacing` stays the canonical path; `ExtensionToolRegistry` continues to own global uniqueness (it already refuses a colliding model-facing name naming both extensions). |
| Activation application point | `ExtensionToolAdapter` gains an internal `ActivationRule`; the loader stamps each adapter's rule from the manifest cross-check. Composition sites filter with one shared helper. |
| Core gate through the evaluator | `ToolActivationEvaluator` replaces `HarnessFactory.LooksLikeCSharpWorkspace`'s inline scan with the same first-party rule (`workspace-root-has-file` over `*.sln|*.slnx|*.csproj`); `AddCSharpTools` stays `[NoInlining]` and Roslyn-free before first call. |
| `--no-extensions` | The three skip sites keep skipping store scans/trust/third-party loads and compose only `BuiltIns`. Skips live at `src/Lunate.Coding/ExtensionSession.cs:34-39`, `PrintMode.cs:157`, `InteractiveSession.cs:579`. |

## 2. Component view

```text
┌──────────────────────── Lunate.Coding (composition) ────────────────────────┐
│ HarnessFactory / InteractiveSession / PrintMode / AcpMode                   │
│  └─ per run: ToolActivationEvaluator("workspace-root-has-file")      [NEW]  │
│       ├─ core cs_* gate (replaces the inline .sln/.slnx/.csproj scan)       │
│       └─ ActiveOnly(tool, root) filter for extension tools         [NEW]    │
│  ExtensionsView: scope `builtin`, inactive(<rule>) report, reload refusal   │
└──────────────┬──────────────────────────────────────────────────────────────┘
               │ uses
┌──────────────▼────── Lunate.Extensibility (host) ───────────────────────────┐
│ ExtensionLoader                                                             │
│  ├─ Discover: BuiltIns first, then store scans (legacy)                     │
│  ├─ Load: Builtin front door (factory in hand, no ALC)   vs directory door  │
│  └─ ActivateAsync: registries + manifest cross-checks (shared tail)         │
│ WorkspaceServiceRegistry: lazy/lease/LRU/pin/shutdown          [NEW]        │
│ ExtensionToolRegistry: alias-aware model-facing names          [MOD]        │
└──────────────┬──────────────────────────────────────────────────────────────┘
               │ depends on
┌──────────────▼────── Lunate.Extensibility.Abstractions (contract) ──────────┐
│ ExtensionApi.Current = "1.12.0"                                             │
│ ExtensionManifest: BuiltIn, WorkspaceServices, ToolActivation, ToolAliases  │
│ IExtensionContext: RegisterWorkspaceService, AcquireWorkspaceServiceAsync   │
│ IWorkspaceServiceFactory<T>, IWorkspaceServiceLease<T>                      │
│ ExtensionToolActivationRule (when/patterns) + ToolActivationEvaluator       │
└─────────────────────────────────────────────────────────────────────────────┘
        (Abstractions keeps its project reference to Lunate.Agent for context types)
```

- **Boundaries.** The contract assembly stays shared with user installs; no replacement message types;
  tools stay `ITool` run by the loop (ADR-0012; no `FunctionInvokingChatClient`, no `AIFunctionFactory`).
- **The checker**: the evaluator is host-side stateless code; an extension never reads the workspace
  for activation.
- **Assumption built-in**: a built-in factory is in-process code the host already ships; the manifest
  is a provenance assertion (`builtin: true`) enforced both ways at load.
- **Open question carried**: workspace-service cap values across `.lunate` settings stay as today; the
  host budget hook is a primitive seam, used by #293.

## 3. Dynamic view: composition and the two front doors

```text
Composition (per session, or per run through CreateWorkspaceTools)
   loader.Discover
      └─ fold BuiltIns ──► id already known from a store scan? ── skip, one error names both
   for descriptor in builtin…global…project:
      directory door: assembly ─► ExtensionLoadContext (collectible) ─► find factory
      builtin door:   BuildIns[i].Factory()                     ─► factory in hand
                    └──────────┐
                               ▼
      ActivateAsync: context(fac: aliases/activation/rules) ─► factory.Create
                     ├─ RegisterWorkspaceService (name from code)  ─► cross-checked
                     ├─ RegisterTool ─► model-facing = alias ?? id__local
                     ├─ CrossCheckManifestTools (asymmetric) ─► refuse/warn/drop
                     └─ background services start, ToolsChanged
   run composition, root = run's worktree root:
      for declared tool with rule T: T false? ─► tool never enters ToolRegistry
                                  └─► /extensions lists `inactive (<rule>): <reason>`
```

## 4. Refusal and report messages (new surface, house style)

| Case | Message |
| --- | --- |
| Directory manifest with `builtin: true` | `<file>: field 'builtin' of a discovered manifest cannot be enabled; built-ins are supplied by the host.` — a discovery error naming the path. |
| Built-in list entry without `builtin: true` | `the built-in manifest '<file>' does not declare "builtin": true; the host composition refused it.` |
| `toolActivation` key not declared | `<file>: field 'toolActivation' declares a rule for '<name>', which is not a declared tool.` |
| Unknown `when` / extra or missing fields | `<file>: field 'toolActivation' rule for '<name>': <one line explaining the condition's fields>.` |
| Pattern path separator or empty patterns | `<file>: field 'toolActivation' rule for '<name>': ...` |
| Alias shape/`__`/invalid | `<file>: field 'toolAliases' for '<name>': alias '<a>' must be 1-64 characters of [A-Za-z0-9_-] and must not contain '__'.` |
| Alias for undeclared key | `<file>: field 'toolAliases' declares an alias for '<name>', which is not a declared tool.` |
| Alias equals core name / canonical name / other alias | `extension '<id>': model-facing name '<a>' was refused: ...` naming both owners (the existing registry refusal style). |
| Registered-but-undeclared workspace service | reuse the tool rule: the load refuses; declared-but-unregistered: logged and dropped. |
| Reload/unload of a built-in | `extension '<id>' is a built-in; built-ins load with the host and cannot be reloaded or unloaded.` |

## 5. Deliberate non-goals (unchanged from the accepted design §9)

No `lunate mcp roslyn` rework (rides with #293); no live mid-run recomposition; no built-in
reload/unload; no remote workspace-service lifecycle; no move of `roslyn.maxLoadedWorkspaces`; the
core `WorkspaceServices` stays for the flagship; no structured-content work (spec §Structured tool
results is decided in the addendum but its implementation is not in tasks.md 4.1 — recorded as a
scope note on #292).
