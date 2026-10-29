# Extension contract: the four gaps before stable

## Why

The extension contract is already published (`ExtensionApi.Current` is `1.11.0`,
`src/Lunate.Extensibility.Abstractions/ExtensionApi.cs:7`), but the product's flagship does not use
it. The surface audit's verdict is *no*: `Lunate.Roslyn` registers nothing through
`IExtensionFactory`/`IExtensionContext`, and the host composes its five `cs_*` tools by hand
(`docs/eval/roslyn-surface-audit.md:26-38`, `src/Lunate.Coding/HarnessFactory.cs:151-160`). The
guide's "built only on the public extension API" claim (`docs/guide.md:19-23`) is therefore not
true today.

Publishing the contract as stable now would freeze four missing primitives. An extension cannot be
built in, cannot own a per-workspace service, cannot say "this tool does not exist in this
workspace", and cannot keep a model-facing name. Each gap has a concrete failure:

- **Built-ins have no path.** Discovery scans only `~/.lunate/extensions` and
  `.lunate/extensions` (`src/Lunate.Extensibility/ExtensionLoader.cs:114-123`), a load requires a
  discovered descriptor (`src/Lunate.Extensibility/ExtensionLoader.Load.cs:32-37`), and a loaded
  extension runs in a collectible context that shares only four assemblies
  (`src/Lunate.Extensibility/HostOwnedAssemblies.cs:28-34`,
  `src/Lunate.Extensibility/ExtensionLoadContext.cs:22-24`). Roslyn must not get a second copy or a
  second process-wide MSBuild bootstrap (`src/Lunate.Roslyn/MsBuildBootstrap.cs:15-28`).
- **Workspace services are host-only.** `WorkspaceServices` already has the lazy creation, LRU cap,
  per-call pinning and disposal semantics ADR-0021 promises
  (`src/Lunate.Coding/WorkspaceServices.cs:18-44`, `:75-79`, `:116-167`, `:235-257`), but it is
  internal and `IExtensionContext` exposes only session-scoped background services
  (`src/Lunate.Extensibility.Abstractions/IExtensionContext.cs:13-23`).
- **A tool is always present.** The core decides whether the C# tools exist by scanning the root
  (`src/Lunate.Coding/HarnessFactory.cs:36-39`, `:127-145`); an extension tool registered through
  `RegisterTool` is offered in every workspace (`src/Lunate.Extensibility/ExtensionToolRegistration.cs:22-53`).
  Without a declared rule, a TypeScript repository sees five dead C# tools.
- **Names are forced.** The model-facing name is `<extension-id>__<local>`
  (`src/Lunate.Extensibility/ExtensionToolNaming.cs:23-25`,
  `src/Lunate.Extensibility/ExtensionToolAdapter.cs:29`). The flagship's names are `cs_*`
  (`src/Lunate.Roslyn/CsDiagnosticsTool.cs:36`). Renaming them would change prompts, TUI tests,
  docs and templates — the one outcome this change must avoid (the audit recommends accepting the
  prefix, `docs/eval/roslyn-surface-audit.md:114-117`; the product constraint overrides that).

## What changes

This is a **design-only** change. It adds no production code, no tests and no migration. It records
the contract in `design.md`, a delta against the `extensions` spec, and a **proposed** addendum to
ADR-0017. The four decisions are:

- **(a) Built-ins register through the same path.** A manifest `builtin: true` marks a
  host-supplied extension. The composition hands `ExtensionLoader` a built-in descriptor plus its
  factory; discovery never reads it from disk; a disk manifest claiming `builtin: true` is refused.
  It runs in the default load context, through the same `IExtensionContext`, registries,
  manifest cross-checks and teardown as a third-party extension. No second Roslyn, no collectible
  ALC, no second trust prompt, no install record. `--no-extensions` keeps built-ins because they
  are host features; it still disables discovery and third-party loading.
- **(b) Per-workspace services.** `IExtensionContext` gains a workspace-scoped registration and
  acquisition API. Values are created lazily per canonical `WorktreeRoot`, capped per service
  (default 2), least-recently-used evicted and disposed; an in-flight lease pins its entry; when
  every slot is pinned, a new workspace waits (cancellation-aware). Eviction disposes resources and
  never deletes durable data — the rule that keeps the `SessionImages` sweep race from becoming a
  service-lifecycle bug.
- **(c) Conditional tool availability.** A manifest rule (`toolActivation`) names one of a closed
  set of workspace conditions. The **host** evaluates it when it composes each run's tool registry;
  extension code is not on the prompt path. A false rule means the tool is absent (not declared,
  not nested, not programmatic). A mid-session condition change is observed at the next
  composition, not by mutating the running registry.
- **(d) `cs_*` stays stable.** The manifest may declare a stable `toolAliases` entry. The alias is
  the model-facing name; the canonical `<id>__<local>` remains for diagnostics and grouping.
  Aliases are globally unique against core names, other aliases and canonical names; collisions
  refuse the later load. The recommendation is aliases, not a reserved unprefixed namespace: it
  gives the flagship the names it has without making one built-in permanently special, and the
  collision rules are explicit. The flagship's aliases are exactly `cs_diagnostics`,
  `cs_find_symbol`, `cs_find_references`, `cs_outline` and `cs_rename`.

## Ordering rule

The contract is **published as stable only after the flagship migrates onto it** and the migration
shows that its behaviour, model-facing tool names and startup/memory/performance budgets are
unchanged. Until then the honest claim remains the audit's: the C# tools are a core capability and
the contract is proven by the fitness suite (`docs/eval/roslyn-surface-audit.md:131-133`).
Implementation and migration are separate, later changes; this one only decides the contract.

## Not using the banned tool plumbing

The design uses none of `FunctionInvokingChatClient` or `AIFunctionFactory`. Every tool remains an
`ITool`; the loop executes it and owns approvals, events, cancellation and error results
(`adr/0012-tool-contract.md:11-13`, `AGENTS.md`). Activation is a host-side filter over
registrations, not automatic function invocation; workspace services are acquired by a tool handler,
not by a function wrapper.

## Capabilities

### Modified Capabilities

- `extensions`: built-in provenance and discovery; per-workspace service lifecycle; manifest-declared
  tool activation; stable tool aliases and their collision rules.

### New Capabilities

- None.

## Impact

- **Contract surface (later implementation):** `ExtensionManifest.BuiltIn`, `ToolActivation`,
  `ToolAliases`, `WorkspaceServices`; `IExtensionContext.RegisterWorkspaceService` and an acquire
  method; a workspace-service lease/factory type; `ExtensionScope.Builtin`. The additions are
  semver-minor and tracked in `src/Lunate.Extensibility.Abstractions/PublicAPI.Unshipped.txt`.
- **Host (later implementation):** the loader's built-in activation path (shared with the existing
  activation tail, `src/Lunate.Extensibility/ExtensionLoader.Load.cs:200-236`), a generalized
  workspace service registry, an activation evaluator and alias collision checks in composition.
- **Docs:** an addendum to `adr/0017-extensibility-architecture.md`; `docs/guide.md`,
  `docs/architecture.md` and `docs/extensions/` are updated by the migration change when the claim
  becomes true, not here.
- **Tests:** none in this change. Each implementation change adds the failing test first; the
  migration change pins `cs_*` names, results and budgets against the pre-migration baseline.
- **Risk:** four additive contract decisions and one `--no-extensions` carve-out for built-ins. The
  addendum is proposed, not accepted; implementation waits for maintainer sign-off.

## Out of scope

- Production code, tests, fixtures and the flagship migration.
- **Structured result data for renderers.** `ToolResult.Details` is UI-only today
  (`src/Lunate.Roslyn/CsDiagnosticsTool.cs:79`, `:96`), and `ToolResultView` carries only
  `ResultText` (`docs/eval/roslyn-surface-audit.md:69-70`). Whether renderers should receive
  `StructuredContent` is an open question for the maintainer; this design does not decide it.
- Moving `roslyn.maxLoadedWorkspaces` out of core settings; reloading a built-in; live mid-run
  activation re-evaluation; remote/out-of-process implementations of per-workspace services;
  TUI rendering changes for `cs_*`.
