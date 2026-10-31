# Design — Roslyn built-in migration

## Context

The migration is the final proof step from archived `extension-contract` design §4.5 and §6 and
`extension-contract-primitives` task 4.2. The host now has the built-in descriptor, workspace-service
lease, manifest activation evaluator, tool filter and alias mechanisms. The flagship still bypasses
them: `HarnessFactory.CreateTools` checks a core rule and calls `AddCSharpTools`
(`src/Lunate.Coding/HarnessFactory.cs:21-50, 134-162`); `WorkspaceServices` owns the Roslyn cache
(`src/Lunate.Coding/WorkspaceServices.cs:18-44`); and the separate `McpRoslynCommand` constructs
Roslyn tools directly (`src/Lunate.Coding/McpRoslynCommand.cs`). Consequently, simply removing the
`Lunate.Coding` → `Lunate.Roslyn` project reference also requires migrating the MCP path and removing
the core workspace registry, not only changing the main tool factory.

`ExtensionSeams.BuiltIns` already carries host-supplied descriptors into the extension session
(`src/Lunate.Coding/ExtensionSession.cs:28-33, 166-195`). `IExtensionContext` provides workspace
service registration/acquisition and file-change subscriptions. The generic registry already enforces
lazy per-root creation, leased pinning, LRU eviction, shutdown and a host cap
(`src/Lunate.Extensibility/ExtensionHostOptions.cs:48-53`,
`src/Lunate.Extensibility/ExtensionWorkspaceServiceRegistry.cs`). The `roslyn.maxLoadedWorkspaces`
setting can therefore remain a host override.

The relevant ADR inventory was checked; none has a `Supersedes` field, so there are no explicit
supersession edges. This design follows accepted ADR-0002 (downward dependencies), ADR-0003 (the
agent owns tool execution), ADR-0005 and its startup-floor addendum, ADR-0006 (in-process Roslyn),
ADR-0009 (memory budgets), ADR-0012 (tool contract), ADR-0015 (session format), ADR-0016 (workspace
boundary), ADR-0017 (extension architecture), ADR-0021 (per-run workspaces) and ADR-0023
(first-party extension tier). The extension primitives' accepted design is archived at
`openspec/changes/archive/2026-10-29-extension-contract/design.md`.

The accepted architecture choice is how `Lunate.Coding` gets a typed built-in factory while having
no direct project reference to `Lunate.Roslyn`. ADR-0026 records maintainer sign-off and its five
constraints; implementation follows those constraints.

### Component view

All components below run in the Lunate process; the MCP server is a separate entry path over the
same host-supplied built-in, not a second Roslyn tool implementation.

```text
┌────────────────────────── Lunate.Coding ───────────────────────────────┐
│ interactive / print / ACP composition             lunate mcp roslyn    │
│           │ built-in descriptor                         │              │
│           └──────────────────────┬───────────────────────┘              │
└──────────────────────────────────┼──────────────────────────────────────┘
                                   ▼
┌────────────── Extension host + ToolActivationEvaluator ────────────────┐
│ registers `roslyn`; filters its tools from manifest rule for each root  │
│ `/extensions`: scope builtin; active or inactive(rule, reason)         │
└──────────────────────────────────┼──────────────────────────────────────┘
                                   │ IExtensionFactory
                                   ▼
┌──────────────── Proposed Lunate.Roslyn.BuiltIn adapter ────────────────┐
│ manifest + factory + cs_* tools; per-call lease from IExtensionContext │
│ one file-change subscription dispatches only to already-created values │
└──────────────────────────────────┼──────────────────────────────────────┘
                                   │ backend
                                   ▼
                         Lunate.Roslyn
                      in-process tools / backend
                                   │ ITool + ToolContext
                                   ▼
                             Agent loop
```

- The composition depends on the extension adapter and the extension host, not directly on
  `Lunate.Roslyn`; the adapter owns the typed factory and is the only integration point for the
  Roslyn implementation.
- Chat, child-workspace composition and MCP use the manifest's one activation rule and the host
  evaluator. No extension code scans a workspace while tool declarations are built.
- The service registry is the sole cache and cap across worktree roots. Event routing may keep a
  lookup of currently-created service values, but it is not a second cache and an event must never
  create a Roslyn service.
- Nothing in the composition may load Microsoft.CodeAnalysis or Microsoft.Build before the first
  `cs_*` call; lazy loading and the existing single-file build-host arrangement remain intact.

## Goals / Non-Goals

**Goals:**

- Make the `roslyn` built-in use the extension contract end to end, including per-workspace service
  leases, manifest activation, aliases, lifecycle and reporting.
- Remove the `HarnessFactory` C# composition/gate and the direct Coding-to-Roslyn project edge,
  including the independent MCP path.
- Prove unchanged names, tool output/error behavior, startup floor and memory against an explicit
  baseline captured before changing composition.
- Keep pre-migration sessions resumable in either direction without changing their format.

**Non-Goals:**

- Change any C# tool behavior, model-facing name, prompt, output schema, settings meaning or session
  format.
- Move `roslyn.maxLoadedWorkspaces` into extension settings or change the cap from two.
- Change the C# activation patterns, add live recomposition, or introduce unload/reload for built-ins.
- Make the extension contract stable; that remains the later `extension-contract-stable` step.
- Add a package dependency or change `lunate mcp roslyn`'s protocol or stdout purity.

## Decisions

### D1 — Put the typed built-in adapter in a separate host integration project (accepted)

Create an internal, non-packable `Lunate.Roslyn.BuiltIn` project that owns the `roslyn` manifest and
`IExtensionFactory`, references `Lunate.Roslyn` and the existing extension contract, and exposes the
host-supplied `BuiltInExtension` descriptor. `Lunate.Coding` references this adapter only to populate
its existing `BuiltIns` seam; it no longer references or names Roslyn implementation types. The
adapter is not a user-installed extension and does not create a collectible load context. The
`Lunate.Roslyn` and adapter project references must remain within the accepted dependency closure:
the extension contract, Roslyn/MSBuild dependencies and the BCL, with no direct edge to
`Lunate.Agent`, `Lunate.Coding` or `Lunate.Extensibility`.

This is the direct, typed composition that fits the existing `BuiltInExtension(Manifest,
Func<IExtensionFactory>)` contract and keeps the host's extension loader generic. Keeping the factory
inside Coding would retain the forbidden dependency. Reflection or a string-named factory would hide
that dependency, weaken compile-time validation and make single-file publish failures harder to
detect. Making `Lunate.Extensibility` depend on the product's Roslyn feature would invert ownership.
The new project adds one small assembly boundary but no NuGet package or public API.

The separate adapter keeps Roslyn implementation types out of `Lunate.Coding`'s compile and load
closure. Its Coding reference has one purpose: supplying the host's built-in factory list. Tests
enforce both the dependency closure and this single-edge rule. ADR-0026 records the maintainer's
five sign-off constraints, including assembly-level proof of lazy loading, unchanged compatibility
and #237 publication ordering.

### D2 — Register one `roslyn` workspace service; preserve event and settings semantics

The adapter registers a service named `roslyn` with cap 2. Its factory creates one lightweight
per-root wrapper around the existing `RoslynBackend`; each of the five handlers acquires the service
for `ToolContext.WorkingDirectory` on every call and disposes the lease at call end. The registry's
existing `WorkspaceServiceCapLimit` receives the current `roslyn.maxLoadedWorkspaces` host setting,
so configuration can lower the manifest cap but cannot raise it. Backend construction and MSBuild
bootstrap remain lazy.

The existing session-level file-change subscription is behaviorally important:
`WorkspaceServices.FileChanges` marks only an existing backend, sends `.cs` paths to
`NotifyFileChanged`, and marks project/solution/build configuration changes for reload. The adapter
subscribes through `IExtensionContext.SubscribeFileChanged`. Its factory tracks only live service
values so the handler can route an event without acquiring (and thereby creating) a service; service
disposal removes that lookup. Tests must cover marks, reload triggers, eviction and shutdown so this
bridge does not silently degrade to stale Roslyn state.

The registry owns all retention, pinning and eviction. The adapter's event lookup is non-owning and
must not keep an evicted service alive. Child runs use their own `ToolContext.WorkingDirectory` and
share the built-in's host registry and cap, as they do today.

### D3 — The manifest is the only chat/MCP activation rule

The built-in manifest declares the default rule `workspace-root-has-file` with `*.sln`, `*.slnx` and
`*.csproj`. Chat/print/ACP/child runs pass the built-in registrations through
`ExtensionToolActivationFilter`; `HarnessFactory.AddCSharpTools` and its parallel gate disappear.
`lunate mcp roslyn` obtains the same built-in tools and asks `ToolActivationEvaluator` for its
client-declared roots, falling back to its launch directory when no roots are declared. A nonmatching
root lists no C# tools; a changed root recomputes the MCP list and sends `tools/list_changed` only
when supported. stdout remains protocol-only. This follows the already accepted extension contract
design §5.5; it is not a new interpretation of the manifest.

### D4 — Measure the migration against a frozen baseline, not code inspection

Before changing composition, capture the current mainline build and exact tool/fixture inputs in
`docs/eval/roslyn-built-in-migration-baseline.md`, with companion deterministic result fixtures under
`tests/fixtures/`. Record commit, SDK/runtime, OS/RID, fixture identity and commands. The evidence
includes:

- all five exact `ToolResult.Output` values and `IsError` flags, plus the five model-facing names;
- the existing 20-run published-binary startup floor and median, using `scripts/perf.sh`'s method;
- idle process-tree peak RSS; and
- Roslyn process-tree peak RSS after loading the reference solution and after ten edit-to-diagnostics
  cycles, using the S-4 sampler method and the ADR-0009 750 MB reference-workspace gate.

Run the same commands on the migrated build under the same conditions and record both sides. Keep
the current per-platform startup limits, idle-memory gate, Roslyn memory gate and two-workspace cap;
report the measured delta rather than inventing new thresholds. The existing `scripts/memory.sh`
Roslyn mode is a TODO, so the implementation task must make the measurement executable (or add an
equivalent committed fixture-based harness) before claiming memory parity. Tests use local fixtures
and a scripted model client only; no live API is involved.

### D5 — Keep session history stable and report the built-in honestly

Aliases pin the existing five `cs_*` names. No session schema or persisted call name changes, so an
old session can resume with the migrated build, and a rollback build can still read sessions written
after migration. A process already running keeps its composed registry; restart/resume is the
boundary, not live replacement. `/extensions` shows one `roslyn` row with scope `builtin`, active
tools for a matching root or the existing inactive rule/reason otherwise, and no trust, uninstall,
unload or reload action. `--no-extensions` continues to include this built-in.

## Risks / Trade-offs

- **A new adapter project is an architecture and dependency-graph change** -> keep it internal and
  non-packable, and enforce the accepted ADR-0026 dependency closure and Coding single-edge rule.
- **Factory creation accidentally loads Roslyn/MSBuild at startup** -> keep backend creation behind
  the service factory, assert assembly absence before first call, and compare the startup floor.
- **File-change routing creates an unbounded second cache or misses updates** -> only index live
  service values, remove them on disposal, never create on events, and test `.cs` marks plus
  solution/project/config reload triggers across eviction.
- **Adapter mapping changes result text or error flags** -> compare every C# tool with the committed
  fixture baseline; do not update the baseline to make a regression pass.
- **MCP behavior differs from chat or writes to stdout** -> compose through the same built-in and
  evaluator, assert root snapshots/notifications, and keep stdout-purity tests.
- **The memory sampler remains a placeholder** -> make the reference-solution run executable before
  the migration is considered verified; an inspection-only claim does not satisfy this design.

## Migration Plan

1. Capture and commit the pre-migration evidence and deterministic tool-result fixtures before any
   composition edits.
2. Add the built-in adapter and prove factory,
   manifest cross-check, lease lifecycle, lazy loading and file-change routing in isolation.
3. Route all coding frontends and `lunate mcp roslyn` through the same built-in/evaluator. Preserve
   the host cap setting and `/extensions` built-in/inactive reporting.
4. Delete `HarnessFactory.AddCSharpTools`, the old `WorkspaceServices` implementation/subscription,
   the direct Coding project reference, and the matching `LayeringChecker` allowance. Prove the
   resulting dependency graph has no direct `Lunate.Coding` → `Lunate.Roslyn` edge.
5. Run exact behavior, resume/rollback, startup and memory comparisons. Update the guide,
   architecture map and MCP tool reference to describe the shipped path.

Rollback is a code revert. There is no session rewrite or new persistent state; aliases preserve
recorded tool names. A running process remains on its original registry until it exits, and a later
resume composes the binary's own tool set.

## Decisions taken

- ADR-0026 is accepted with maintainer sign-off dated 2026-10-30. Its five constraints govern the
  implementation; the adapter remains the typed boundary selected by this design.
- Keep the existing two-workspace cap, compatibility surface and session format. Restore the guide's
  “built only on the public extension API” positioning only after the dependency-closure,
  single-edge, assembly-level lazy-loading and unchanged compatibility/golden/e2e checks pass. Issue
  #237 may publish `Lunate.Extensibility.Abstractions` as stable only after that sentence is restored.
- The card forbids delegation, so the implementation and acceptance checks are carried out in this
  session without subagents.
