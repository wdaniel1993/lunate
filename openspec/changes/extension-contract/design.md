# Design — extension contract: built-ins, workspace services, activation and stable names

- Status: proposed — 2026-10-29
- Change: `extension-contract` (#268 step 2)
- Evidence: [`docs/eval/roslyn-surface-audit.md`](../../../docs/eval/roslyn-surface-audit.md) —
  the four gaps, each with `file:line`
- Amends (proposed): [ADR-0017](../../../adr/0017-extensibility-architecture.md) (contract and
  loading, workspaces), and through it ADR-0021 (per-workspace services) and ADR-0006 (Roslyn
  placement)
- Design only: no production code, tests, fixtures or migration are part of this change

## 1. Constraints and the ordering rule

The contract is already published (`ExtensionApi.Current` is `1.11.0`,
`src/Lunate.Extensibility.Abstractions/ExtensionApi.cs:7`) and the guide already describes the
extension model (`docs/guide.md:462-470`). The audit's verdict is that the flagship uses none of it
(`docs/eval/roslyn-surface-audit.md:26-43`). Four primitives are missing
(`docs/eval/roslyn-surface-audit.md:53-74`):

1. a built-in registration path that does not scan the store or use a collectible load context;
2. a per-workspace service facility exposed to extensions;
3. a host-evaluated activation rule for tools;
4. a way to keep the model-facing `cs_*` names without renaming prompts, docs and tests.

**The ordering rule.** This contract is published as **stable only after** the flagship
(`Lunate.Roslyn`) migrates onto it and the migration proves three equalities against a recorded
baseline:

- **behaviour**: the same `ToolResult` text and `IsError` values for the same fixtures;
- **tool names**: exactly `cs_diagnostics`, `cs_find_symbol`, `cs_find_references`, `cs_outline`
  and `cs_rename` (the names in `src/Lunate.Roslyn/CsDiagnosticsTool.cs:36` and its four siblings);
- **budgets**: startup, idle memory and the first-call latency stay within the existing ADR-0005
  and ADR-0009 gates, including the "nothing loads before the first `cs_*` call" rule
  (`src/Lunate.Coding/WorkspaceServices.cs:275-280`).

Until that migration is green, the honest statement remains the audit's: the C# tools are a core
capability and the contract is proven by the fitness suite
(`docs/eval/roslyn-surface-audit.md:131-133`). Implementation of the four primitives and the
migration are separate, later changes; this change only fixes the contract they must implement.

**Banned plumbing.** Nothing here uses `FunctionInvokingChatClient` or `AIFunctionFactory`. Tools
stay `ITool` and are executed by the loop (`adr/0012-tool-contract.md:11-13`); activation is a
host-side filter and workspace services are acquired inside a handler.

## 2. What exists today

| Gap | Current code | Consequence |
| --- | --- | --- |
| Built-ins | Discovery scans only `~/.lunate/extensions` and `.lunate/extensions` (`src/Lunate.Extensibility/ExtensionLoader.cs:114-123`); `Load` requires a discovered descriptor (`src/Lunate.Extensibility/ExtensionLoader.Load.cs:32-37`); third-party loads always create a collectible context (`src/Lunate.Extensibility/ExtensionLoader.Load.cs:140`); only four assemblies are shared (`src/Lunate.Extensibility/HostOwnedAssemblies.cs:28-34`) | Roslyn cannot be an extension without a second copy or a second ALC. The host composes it directly (`src/Lunate.Coding/HarnessFactory.cs:151-160`) and lays a host edge in the layering test (`tests/Lunate.Coding.Tests/LayeringChecker.cs:54-55`). |
| Workspace services | `WorkspaceServices` is internal, keyed by canonical root, lazy, LRU-capped, pinned per call and disposed on eviction/shutdown (`src/Lunate.Coding/WorkspaceServices.cs:18-44`, `:75-79`, `:116-167`, `:235-257`). `IExtensionContext` has only session-scoped `RegisterService`/`IBackgroundService` (`src/Lunate.Extensibility.Abstractions/IExtensionContext.cs:19-23`, `src/Lunate.Extensibility.Abstractions/Services.cs:8-13`) | An extension must own a cache in a background service and has no eviction or pinning; ADR-0021's amendment explicitly says extensions have no such API (`adr/0021-per-run-workspaces.md:32`). |
| Activation | The core scans the root for `.sln`/`.slnx`/`.csproj` (`src/Lunate.Coding/HarnessFactory.cs:36-39`, `:127-145`); extension tools are registered unconditionally (`src/Lunate.Extensibility.Abstractions/ExtensionToolRegistration.cs:22-53`) and added to every run (`src/Lunate.Coding/PrintMode.cs:213-214`, `src/Lunate.Coding/AcpMode.cs:79-80`, `src/Lunate.Coding/InteractiveSession.cs:683-684`) | A non-C# repository sees five dead C# tools; there is no way for any extension to say "not here". |
| Names | Model-facing names are composed as `<id>__<local>` (`src/Lunate.Extensibility.Abstractions/ExtensionToolNaming.cs:18-25`, `src/Lunate.Extensibility/ExtensionToolAdapter.cs:29`); the flagship's names are `cs_*` (`src/Lunate.Roslyn/CsDiagnosticsTool.cs:36`); the TUI keys argument summaries and block headers off `cs_` (`src/Lunate.Tui/ToolBlocks/ToolArgsSummary.cs:125`, `src/Lunate.Tui/ToolBlocks/CsToolMeta.cs:10`) | Renaming the tools changes prompts, TUI tests, docs and templates. The audit's preferred `roslyn__cs_*` (`docs/eval/roslyn-surface-audit.md:114-117`) is excluded by the product constraint. |

## 3. (a) Built-ins register through the same path

### 3.1 What "built-in" means in the manifest

A manifest may declare `"builtin": true` (optional boolean, default `false`). It means:

- the extension ships with the host and its assembly is already part of the host's default
  `AssemblyLoadContext`;
- its lifecycle and version are the host's; it is never installed, trusted by content hash,
  upgraded or uninstalled independently;
- it is supplied by the host composition, not discovered from a directory;
- it still declares `tools`, `commands`, `hooks`, `services`, `workspaceServices`,
  `toolActivation` and `toolAliases` exactly like any other extension, and the same asymmetric
  cross-checks apply (`src/Lunate.Extensibility/ExtensionLoader.Load.cs:322-356`).

`builtin: true` is a provenance assertion, not a privilege a directory can claim. The parser accepts
it only on a manifest supplied through `ExtensionHostOptions.BuiltIns`; a manifest found by
`ScanDirectory` with `builtin: true` is a discovery error naming the path and the reason. This keeps
the trust story intact: a user cannot promote a directory extension to built-in by editing JSON.

### 3.2 Discovery differs from installation

- **Installation** writes a directory under `~/.lunate/extensions` or `.lunate/extensions`, records
  a package binding and asks the trust question for a project or installed extension
  (`src/Lunate.Extensibility/ExtensionLoader.Load.cs:64-96`).
- **Built-in discovery** is a fixed list supplied by the composition:
  `ExtensionHostOptions.BuiltIns` carries a `BuiltInExtension` — manifest plus a
  `Func<IExtensionFactory>` — in a deterministic order. `Discover` folds these descriptors into
  `_descriptorsById` before scanning the two store scopes
  (`src/Lunate.Extensibility/ExtensionLoader.cs:109-123`).
- A discovered descriptor whose id equals a built-in id is skipped with one discovery error naming
  both sources. Built-ins win; a user cannot shadow a built-in by placing a directory.
- Built-ins have no install record, no content hash and no trust prompt. `/extensions` shows scope
  `builtin`, never `pending` or `trust-denied`, and offers no uninstall/reload action for them.
- `--no-extensions` still composes the built-in set, because what the product ships with is not a
  user extension. It continues to skip discovery, trust, third-party loads, extension MCP servers,
  renderers and widgets; a built-in that contributes any of those must keep its behaviour identical
  to today (the flagship contributes tools only). This preserves the current C# behaviour under
  `--no-extensions`: the C# tools are present today because they are core
  (`src/Lunate.Coding/HarnessFactory.cs:36-39`), and they remain present after migration. The
  current `--no-extensions` skip is at `src/Lunate.Coding/InteractiveSession.cs:579`,
  `src/Lunate.Coding/PrintMode.cs:157` and `src/Lunate.Coding/ExtensionSession.cs:34-39`.

### 3.3 Loading and lifecycle

The loader's activation tail is shared, not duplicated
(`src/Lunate.Extensibility/ExtensionLoader.Load.cs:200-236`):

```
Discover() ──> descriptor (Builtin | Global | Project)
                   │
                   ├─ Builtin: factory supplied in-process, no directory, no ALC
                   │            │
                   └─ Global/Project: assembly ──> ExtensionLoadContext (collectible)
                                │
                                ▼
                    ActivateAsync: ExtensionContext -> factory.Create
                                -> manifest cross-checks -> registries
                                -> background services / ToolsChanged
```

The refactor splits the current `Load` into a common activation tail and two front doors:

- the existing directory/ALC front door (unchanged for third-party extensions);
- a built-in front door that resolves the supplied factory, creates the same `ExtensionContext`
  and calls the same `ActivateAsync`. It never calls `LoadFromAssemblyPath` and never constructs
  `ExtensionLoadContext` (`src/Lunate.Extensibility/ExtensionLoader.Load.cs:140`,
  `src/Lunate.Extensibility/ExtensionLoadContext.cs:22-24`).

Built-ins load first, in the composition's declared order, then global, then project descriptors
(the scan already sorts directories ordinally, `src/Lunate.Extensibility/ExtensionLoader.cs:345-347`).
This order is visible to hooks and to alias/tool collision resolution.

### 3.4 No second Roslyn, no second MSBuild bootstrap

- Because a built-in lives in the default context, `Lunate.Roslyn` binds to the host's
  `Lunate.Agent` and `Lunate.Ai` copies. The four-assembly sharing rule for third-party contexts is
  untouched (`src/Lunate.Extensibility/HostOwnedAssemblies.cs:28-34`).
- The built-in is the only Roslyn in the process. It must not ship a private copy, and the host must
  not load a second copy through a directory scan (the id-collision rule above prevents it).
- `MSBuildLocator.RegisterDefaults()` is process-wide and already guarded once per process
  (`src/Lunate.Roslyn/MsBuildBootstrap.cs:15-28`). The built-in continues to call
  `MsBuildBootstrap.EnsureInitialized()`; the loader does not add another bootstrap, and the
  built-in's factory does not touch Roslyn or MSBuild before the first call
  (`src/Lunate.Coding/WorkspaceServices.cs:275-280`).
- A built-in is not unloaded or reloaded in v1: there is no ALC to collect and reloading would
  re-run a factory against process-wide state. Session end stops its background services; `/extensions
  reload` refuses a built-in with a teaching message.

### 3.5 Failure modes

- A built-in factory that throws fails that extension's load without aborting the session, like any
  other load failure (`src/Lunate.Coding/ExtensionSession.cs:259-272`).
- A built-in manifest that is malformed is a discovery/load error naming the manifest source.
- A built-in that registers an undeclared tool refuses the load (the same asymmetric rule,
  `src/Lunate.Extensibility/ExtensionLoader.Load.cs:322-356`).
- A built-in that fails to start a background service is reported and its remaining services do not
  start, matching the existing service policy (`openspec/specs/extensions/spec.md:393-404`).

## 4. (b) Per-workspace services

### 4.1 Contract

`IExtensionContext` gains a generic, registration-only workspace service seam:

```csharp
public interface IWorkspaceServiceFactory<T>
    where T : class
{
    ValueTask<T> CreateAsync(string worktreeRoot, CancellationToken cancellationToken);
}

public interface IWorkspaceServiceLease<out T> : IAsyncDisposable
    where T : class
{
    T Value { get; }
}

// on IExtensionContext
void RegisterWorkspaceService<T>(string name, IWorkspaceServiceFactory<T> factory)
    where T : class;

ValueTask<IWorkspaceServiceLease<T>> AcquireWorkspaceServiceAsync<T>(
    string name,
    string worktreeRoot,
    CancellationToken cancellationToken)
    where T : class;
```

- `T` must implement `IAsyncDisposable`; an `IDisposable`-only value is wrapped by the extension.
  The host disposes on eviction and shutdown.
- `worktreeRoot` is the key. The host canonicalizes it exactly as `WorkspaceServices.BackendFor`
  does (`src/Lunate.Coding/WorkspaceServices.cs:75-79`) and compares it with the platform rule
  already used (`src/Lunate.Coding/WorkspaceServices.cs:259-260`).
- The factory is registration only: no value is created, no process starts, no file is read.
- The manifest declares the service and its cap:

```json
"workspaceServices": [
  { "name": "roslyn", "maxLoadedWorkspaces": 2 }
]
```

The rule is the tool rule: a registered-but-undeclared service refuses the load; a
declared-but-unregistered service is logged and dropped. `maxLoadedWorkspaces` is a positive
integer; absent means **2** (ADR-0021's default, `adr/0021-per-run-workspaces.md:23`). The cap is
per (extension, service name). The host may lower it through its own budget; an extension cannot
raise it above the host maximum.
- A lease is the pinning unit. A tool handler acquires per call and disposes the lease in a `using`
  or `await using`; holding a lease across model turns is legal but pins the entry and can stall
  another workspace (see 4.3).
- v1 is in-process only. An out-of-process extension serves a workspace service through the existing
  `IJsonService` handle shape; the host-side lifecycle messages (`workspace/acquire`,
  `workspace/evict`, `workspace/stop`) are a later remote change, not part of this contract.

### 4.2 Lifecycle

The registry is a per-extension generalization of `WorkspaceServices`
(`src/Lunate.Coding/WorkspaceServices.cs:18-44`):

1. **Registration** records `(extensionId, name, factory, cap)`; nothing is created.
2. **Acquire** canonicalizes the root and looks up the entry.
   - No entry and a free slot: create the value lazily, outside the lock; insert with a
     double-check so a concurrent creator's value is disposed, not leaked
     (`src/Lunate.Coding/WorkspaceServices.cs:174-207`).
   - No entry and the cap held: evict the least-recently-used **unpinned** entry; dispose it outside
     the lock (`src/Lunate.Coding/WorkspaceServices.cs:140-158`, `:235-257`).
   - Every slot pinned: wait in short slices, cancellation-aware, rather than unloading under a
     live call (`src/Lunate.Coding/WorkspaceServices.cs:20-21`, `:147-151`).
   - Pin the entry and return a lease (`src/Lunate.Coding/WorkspaceServices.cs:137-138`).
3. **Release** decrements the pin and wakes waiters
   (`src/Lunate.Coding/WorkspaceServices.cs:209-217`).
4. **Evict** removes the entry, marks it evicted, records it for tests, and disposes the value
   outside the lock (`src/Lunate.Coding/WorkspaceServices.cs:235-257`).
5. **Shutdown** marks the registry disposed, releases all waiters and disposes every entry;
   a later acquire throws `ObjectDisposedException`
   (`src/Lunate.Coding/WorkspaceServices.cs:83-109`, `:125`).

### 4.3 Eviction with a pinned consumer, workspace switch and shutdown

- **Pinned consumer.** A pinned entry is never evicted. If every loaded slot is pinned, a new
  root's acquisition waits instead of disposing a value under a live call. The wait is bounded only
  by the caller's cancellation token; a pathological pin storm degrades to slowness, not errors
  (`adr/0021-per-run-workspaces.md:24`, `:39`). The host does not time out a lease; the consumer
  owns the duration.
- **Workspace switch.** There is no global "current workspace" in the registry; each acquisition
  names its root, so a switch is just an acquisition for a different key. The old root's entry stays
  loaded and becomes the LRU candidate; it is reloaded (from disk) if a later call returns to it.
  The session's root and a child run's root share the same registry and therefore the same cap
  (`adr/0021-per-run-workspaces.md:15-25`). A long-lived lease on the old root can keep it pinned
  and make the new root wait — the reason handlers acquire per call.
- **Shutdown.** The session cancels in-flight work first, then the registry disposes every entry,
  pinned or not, and releases waiters. A call that resumes after shutdown fails with
  `ObjectDisposedException`; the tool adapter turns that into a cancelled/teaching result rather
  than a crash (the equivalent current path is `src/Lunate.Extensibility/ExtensionToolAdapter.cs:61-78`).
  Family: the service's `DisposeAsync` must be idempotent and must not throw.

### 4.4 The data-loss rule (the `SessionImages` failure class)

Eviction may discard **rebuildable resources**; it must never delete **durable data**. This is a
hard rule, because the product has already seen the failure class in `SessionImages`:

- `SessionImages.Store` writes a content-addressed blob and returns an `ImageContentReference`
  (`src/Lunate.Agent/SessionImages.cs:96-150`); the reference is only attached to a message later
  (`src/Lunate.Agent/AgentHarness.Tools.cs:138`).
- `Session.Load` sweeps blobs whose digest is not referenced by any entry
  (`src/Lunate.Agent/Session.cs:252-255`), and `Sweep` deletes those files
  (`src/Lunate.Agent/SessionImages.Sweep.cs:64-99`).
- If another process (or a later open of the same session) sweeps between the store and the append,
  the just-written blob is unreferenced and is deleted; the writer's message then points at a
  missing file. The archive design records the single-writer assumption and this race
  (`openspec/changes/archive/2026-10-22-images/design.md:154`).
- `ResetForNewSession` deletes the session's whole image directory
  (`src/Lunate.Agent/SessionImages.cs:44-58`), the most direct example of a lifecycle action deleting
  durable bytes.

Therefore:

- A workspace service owns no session file and no shared content-addressed store. Its state is a
  cache of handles that can be rebuilt from disk.
- Eviction disposes the value; it does not sweep, garbage-collect or reset a directory. A service
  that writes its own files under a host-owned workspace cache may delete only files it is the sole
  writer of, and only when the workspace is removed — never on LRU.
- Session-scoped data (images, spill files, session JSONL) has a session lifetime. It is not a
  per-workspace service and is not evicted with one.
- If a future service needs reference-counted cleanup, it must use a reference check that is
  computed under the writer's own consistency boundary (the session's single writer), not an
  on-eviction guess.

### 4.5 Roslyn as the first consumer

- The built-in Roslyn service is named `roslyn`; its manifest declares
  `{ "name": "roslyn", "maxLoadedWorkspaces": 2 }`.
- Each `cs_*` handler acquires the lease for `ctx.WorkingDirectory` (the run's workspace root) per
  call, calls the backend, and disposes the lease. It never holds the lease across calls.
- The cap, pinning, wait and dispose paths are the ones already implemented
  (`src/Lunate.Coding/WorkspaceServices.cs:116-167`, `:235-257`); the current `BackendFor` handle
  (`:75-79`, `:319-375`) is the shape the generic lease generalizes.
- The existing `roslyn.maxLoadedWorkspaces` setting
  (`src/Lunate.Coding/SettingsStore.cs:68`, `:564`) remains a host override during migration. Moving
  it into the extension's `settingsSchema` is the audit's P2 item
  (`docs/eval/roslyn-surface-audit.md:122-125`) and is **not** decided here.

## 5. (c) Conditional tool availability

### 5.1 Manifest rule

`tools` stays the declared-name list. A new optional object, `toolActivation`, maps a rule key to
a condition:

```json
"tools": ["cs_diagnostics", "cs_find_symbol", "cs_find_references", "cs_outline", "cs_rename"],
"toolActivation": {
  "*": {
    "when": "workspace-root-has-file",
    "patterns": ["*.sln", "*.slnx", "*.csproj"]
  }
}
```

- `*` is the default rule for every declared tool without its own entry. A key that is not `*` or a
  declared tool name is a parse error naming the manifest and the field.
- A tool with no rule (and no `*`) is always available. This keeps every existing manifest
  working.
- A rule is a JSON object with `when` and the fields that condition needs. Unknown `when` values
  are parse errors; unknown condition fields are parse errors. The manifest is what the user
  trusted, so the rule is validated before any code loads.

### 5.2 The closed condition set

| `when` | Fields | True when |
| --- | --- | --- |
| `always` | none | Always. |
| `workspace-root-has-file` | `patterns` (non-empty string array) | At least one direct child of the worktree root matches a pattern. |
| `workspace-is-git-repository` | none | The run's workspace has a `GitCommonDir` (`src/Lunate.Coding/Workspace.cs:26`). |

`workspace-root-has-file` is deliberately narrow: direct children only, no recursion, no `**`, and
patterns are file names with `*`/`?` wildcards. A pattern containing `/` or `\` is refused. Matching
is ordinal, which is what the existing extension check does (`Path.GetExtension(file) is ".sln" or
".slnx" or ".csproj"`, `src/Lunate.Coding/HarnessFactory.cs:131-137`). An unreadable root evaluates
false, matching today's catch (`src/Lunate.Coding/HarnessFactory.cs:139-142`).

The flagship's rule is exactly the existing gate: `.sln`, `.slnx` or `.csproj` at the root. The
evaluator lives in the host; the extension never scans.

### 5.3 Where and when the host evaluates it

The host evaluates activation when it **composes a run's tool registry**, per workspace:

- interactive session start (`src/Lunate.Coding/InteractiveSession.cs:465-495`);
- print mode (`src/Lunate.Coding/PrintMode.cs:616-650`);
- ACP mode (`src/Lunate.Coding/AcpMode.cs:250-270`);
- every child run, through `CreateWorkspaceTools`
  (`src/Lunate.Coding/HarnessFactory.cs:89-120`);
- an extension load/unload or reload that recomposes the set.

The result is a filtered list: an inactive tool is not added to the run's `ToolRegistry`
(`src/Lunate.Agent/ToolRegistry.cs:25-39`). It is therefore absent from the model's declarations,
from `ExecuteToolAsync` and from `ToolContext.ToolStubs`; a resumed session that calls it renders
as an unknown tool, like any tool that is not present. The tool remains registered in the extension
registry, so `/extensions`, unload and the manifest cross-check still see it; only the run's offered
set is narrowed.

**A condition changing mid-session.** In v1, a running registry is not mutated. Adding a `.sln` to a
TypeScript workspace after the session started does not make the C# tools appear in that run; the
next composition — a child run in that root, an extension reload, or a new session — sees the new
condition. This is the current behaviour of the core gate, which runs once per composition
(`src/Lunate.Coding/HarnessFactory.cs:36-39`), and it keeps the prompt prefix and the declared tool
set stable for the life of a run. Live re-evaluation during a run would have to rewrite tool
declarations and the cacheable prefix; it is a separate decision, not this contract.

**Why the host evaluates it.** The manifest is the trusted, pre-code declaration; a rule in it is
reviewable and content-hashed with the rest of the extension, while extension code on the
prompt-build path would be an unobservable side effect. One host evaluator gives the core gate, the
built-in and third-party tools the same semantics, keeps the decision out of the model request, and
lets `/extensions` report a stable reason. It also keeps `FunctionInvokingChatClient` and
`AIFunctionFactory` out of the design: the host filters registrations, the loop still executes
`ITool`.

### 5.4 Visibility and failures

- `/extensions` shows a declared tool whose rule is false as `inactive (<condition>): <reason>`; a
  true rule shows `active`. Registered/declared counts do not change.
- A malformed rule refuses the extension's load (or discovery, for a manifest parse error) naming
  the manifest, the tool and the field.
- An unreadable root is inactive with a reason, never a crash; the session continues.
- The activation result is cached per composition; `ToolsChanged` fires only when a load/unload or
  reload actually changes the registered set, not on every composition.

## 6. (d) `cs_*` stays stable: the mechanism

### 6.1 The two options

**Option A — a reserved unprefixed namespace.** The host reserves `cs_` for one built-in and lets it
publish local names without the `<id>__` prefix. Names stay `cs_*`, but the namespace rule gains a
permanent exception; every future first-party tool would want the same hole, and a third-party tool
with a `cs_` name would need a special refusal. The audit already warns that a first-party exception
weakens the contract (`docs/eval/roslyn-surface-audit.md:114-117`), even though it recommends the
opposite trade for the prefix itself.

**Option B — extension-declared stable aliases.** A manifest maps local names to model-facing
aliases:

```json
"tools": ["cs_diagnostics", "cs_find_symbol", "cs_find_references", "cs_outline", "cs_rename"],
"toolAliases": {
  "cs_diagnostics": "cs_diagnostics",
  "cs_find_symbol": "cs_find_symbol",
  "cs_find_references": "cs_find_references",
  "cs_outline": "cs_outline",
  "cs_rename": "cs_rename"
}
```

The canonical name stays `<id>__<local>` internally (`ExtensionToolAdapter.ExtensionId`/`LocalName`,
`src/Lunate.Extensibility/ExtensionToolAdapter.cs:32-38`); the alias is what the model sees. The
namespace stays the extension id (`src/Lunate.Extensibility/ExtensionToolAdapter.cs:46`), so
discovery grouping and diagnostics do not change. A tool with no alias keeps the canonical name.

### 6.2 Recommendation

**Option B.** Reasons:

- It keeps the namespace rule intact for everyone. The flagship is not special; it simply declares
  the names it publishes, as any extension can.
- The exception is visible in the trusted manifest, reviewed with the tool list, and content-hashed
  with the extension. A reserved prefix is an invisible policy in the host.
- Collision rules are the same global uniqueness rules the tool registry already needs, so the
  mechanism generalizes to future renames and to tools that predate a namespace.
- The cost is one optional manifest field and one validation path, versus a permanent hole in the
  naming contract. `cs_*` is the only name set that must stay stable now; aliases solve it without
  deciding that first-party tools are a different kind of tool.

Option A is rejected because it makes the tool name depend on who shipped the extension rather than
on what the manifest declares, and the audit's contract goal is the opposite
(`docs/eval/roslyn-surface-audit.md:40-43`).

### 6.3 Collision rules

- An alias must match `[A-Za-z0-9_-]{1,64}`, must not contain `__` (the canonical separator,
  `src/Lunate.Extensibility.Abstractions/ExtensionToolNaming.cs:18-25`), and must not be a core tool
  name. A core name is reserved; an alias that equals one refuses the load before composition.
- Aliases are globally unique across built-ins, extensions and canonical names. The later load is
  refused with one message naming both owners. Built-ins load first, so a discovered extension can
  never take `cs_*` from the flagship; if a user already had an extension using one of those names,
  its next load fails with a message that says which built-in owns the name.
- An alias that collides with an MCP tool's model-facing name follows the existing composition
  rule: extension tools compose first, the MCP tool is skipped with one single-line diagnostic
  naming both sources (`openspec/specs/extensions/spec.md:725-729`). Extension-vs-extension and
  extension-vs-core collisions are load failures, not first-wins, because the losing extension
  would silently lose a model-facing surface.
- `toolAliases` keys must be declared in `tools`; an alias for an undeclared or unregistered tool is
  a manifest error. Duplicate keys are refused by the JSON object itself; duplicate local names are
  already refused (`src/Lunate.Extensibility/ExtensionToolRegistry.cs:35-58`).
- An alias is part of the extension's published surface. Changing or removing one is a breaking
  change to every prompt and template that names it; the host cannot enforce that, so the contract
  states it and the built-in's aliases are pinned by the migration's tests. Canonical names may
  change with the extension id without changing the alias.

### 6.4 What does not change

The TUI's name-based summaries (`src/Lunate.Tui/ToolBlocks/ToolArgsSummary.cs:125`,
`src/Lunate.Tui/ToolBlocks/CsToolMeta.cs:10`) and the result renderers are not touched by aliases;
the model-facing string is still `cs_*`. Whether renderers should receive structured result data is
the open question below, deliberately not decided here.

## 7. Rollout

| Phase | Change | Contract state |
| --- | --- | --- |
| 1 (this change) | Design only: proposal, delta spec, this design, proposed ADR addendum | Not stable; no production code |
| 2 | Implement the four primitives behind the current host composition; add red-green tests; keep the flagship where it is | Additive, still not the stable claim |
| 3 | Migrate `Lunate.Roslyn` to the built-in path, workspace service, activation rule and aliases; prove behaviour/names/budgets equal | Migration is the proof |
| 4 | Publish stable: close the audit's P1 items, update the guide and architecture claims, declare the contract's semver stability | Stable only now |

Phase 2 and 3 are separate OpenSpec changes and separate PRs. The API version moves semver-minor
for the additive contract in phase 2 (`ExtensionApi.Current`, `src/Lunate.Extensibility.Abstractions/ExtensionApi.cs:7`);
the exact version is set in that change.

## 8. Deliberate non-goals and open questions

**Not done here (and not implied by the contract):**

- No production code, test, fixture, migration or doc-claim change in this change.
- No moving of `roslyn.maxLoadedWorkspaces` out of core settings; it stays the host override during
  migration (`src/Lunate.Coding/SettingsStore.cs:68`, `:564`).
- No live mid-run activation re-evaluation; conditions are read at composition (5.3).
- No built-in reload or unload; built-ins are process-lifetime code (3.4).
- No remote/out-of-process per-workspace service lifecycle in v1 (4.1).
- No `FunctionInvokingChatClient`, no `AIFunctionFactory`, no parallel tool-execution layer.

**Open question for the maintainer — structured result data for renderers.** `ToolResult.Details`
is UI-only and typed today (`src/Lunate.Roslyn/CsDiagnosticsTool.cs:79`, `:96`), and extension
renderers receive only `ResultText` (`docs/eval/roslyn-surface-audit.md:69-70`). Should
`ToolResultView` carry `StructuredContent` so a renderer can render a result without parsing text?
That is the audit's P1 renderer-parity item (`docs/eval/roslyn-surface-audit.md:118-121`). It is
recorded here as **open**; this design does not decide the data shape, the JSON contract or the
renderer API.

## 9. Verification of this change

- `openspec validate --all --strict` passes.
- `python3 scripts/docs-sync --check` passes (no generated regions change).
- `npx --yes markdownlint-cli2@0.23.3` passes for the ADR addendum (`openspec/` is excluded).
- Every claim about current code in this change carries a `file:line`.
- The addendum is marked **proposed**; no production file, test or fixture is touched.
