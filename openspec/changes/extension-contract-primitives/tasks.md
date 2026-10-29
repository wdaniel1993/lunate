# Tasks — extension-contract primitives

Test-first: every task lands as a failing test proved at the reported condition, then the
implementation (senior-dev subagent batches; one batch per section, interleaved as needed).

## 1. The API version

- [x] 1.1 Red→green: `tests/Lunate.Extensibility.Tests/ExtensionApiTests.cs` pins
      `ExtensionApi.Current == "1.12.0"` and the contract-assembly version equality; update
      `src/Lunate.Extensibility.Abstractions/PublicAPI.Unshipped.txt`; the three docs that still name
      1.10.0 (`docs/extensions/manifest.md`, `lifecycle-and-trust.md`, `troubleshooting.md`) name
      1.12.0 and the current refusal wording.

## 2. Manifest fields parse and validate

- [x] 2.1 Red→green: `builtin` (default false) parses; a directory manifest declaring it is a
      discovery error naming the path and the reason; a built-in list manifest without it is refused
      naming the manifest and the field.
- [x] 2.2 Red→green: `workspaceServices` parses name + optional positive `maxLoadedWorkspaces`
      (default 2); duplicates and non-positive/non-integer values refuse with the manifest name and
      field.
- [x] 2.3 Red→green: `toolActivation` parses the closed set `always`, `workspace-root-has-file`
      (non-empty `patterns`, no `/` or `\`), `workspace-is-git-repository`; unknown `when`, unknown or
      missing condition fields, and a key outside `*` ∪ declared tools refuse naming the manifest, the
      tool key and the field.
- [x] 2.4 Red→green: `toolAliases` parses `[A-Za-z0-9_-]{1,64}` aliases without `__`, keys ⊆ declared
      tools, duplicate keys refuse with the manifest-parser's duplicate style.

## 3. The built-in path

- [x] 3.1 `ExtensionScope.Builtin`, a `BuiltInExtension` (manifest + `Func<IExtensionFactory>`) and
      `ExtensionHostOptions.BuiltIns` / `ExtensionTestHostOptions.BuiltIns`; red→green: built-ins
      fold in before store scans, in declared order, on `Discover` with no directory.
- [x] 3.2 Red→green: a discovered id equal to a built-in id is skipped with one discovery error naming
      both sources; the built-in wins.
- [x] 3.3 Red→green: the built-in front door runs the same activation tail (factory in hand, no
      collectible ALC created, no trust, no install record, no content hash), refuses cross-check
      failures like a discovered extension and fails one built-in without aborting the session.
- [x] 3.4 `--no-extensions` composes only the built-in set; red→green prove the three skip sites
      (`ExtensionSession`, `PrintMode`, `InteractiveSession`) leave discovery, trust and third-party
      loads off while the built-in's tools register and run.
- [x] 3.5 Red→green: `/extensions` shows scope `builtin` (never `pending`/`trust-denied`), and
      unload/`extensions reload <id>` of a built-in is refused with a teaching message.

## 4. Per-workspace services

- [x] 4.1 Red→green: `IWorkspaceServiceFactory<T>` / `IWorkspaceServiceLease<T>` and
      `IExtensionContext.RegisterWorkspaceService` / `AcquireWorkspaceServiceAsync` compile against a
      failing consumer and appear in `PublicAPI.Unshipped.txt`.
- [x] 4.2 Red→green: the host-side registry lifecycle — lazy create per canonical root outside the
      lock, dispose on eviction/shutdown, pinned lease never evicted while an unpinned LRU candidate
      is, all-pinned acquisition waits cancellation-aware, `ObjectDisposedException` after shutdown,
      lease dispose wakes waiters.
- [x] 4.3 Red→green: the cap binds per (extension, service) from the manifest with default 2, the host
      may lower it, and evictions never sweep or delete files (dispose of the value only).
- [x] 4.4 Red→green: registered-but-undeclared service refuses the load; declared-but-unregistered is
      logged and dropped — the tool rule extended to services.

## 5. The activation evaluator

- [ ] 5.1 Red→green: the evaluator in `Lunate.Extensibility.Abstractions` — `always` true,
      `workspace-root-has-file` matches direct children only (no recursion, ordinal, no path
      separator), unreadable root is false without throwing, `workspace-is-git-repository` follows
      `GitCommonDir`.
- [ ] 5.2 Red→green: the loader stamps each registered tool's rule from the manifest; composition
      filters per run root through one shared helper, so an inactive tool never enters the
      registry, the model declarations, nested calls or `ToolContext.ToolStubs`, and a mid-session
      change waits for the next composition.
- [ ] 5.3 Red→green: the core gate in `HarnessFactory` expresses the flagship rule through the
      evaluator (same matching, unreadable root false, Roslyn still not loaded before first use per
      `tests/Lunate.Roslyn.Tests/LazyLoadingTests.cs`).
- [ ] 5.4 Red→green: `/extensions` reports `inactive (<condition>): <reason>` for a false rule, an
      active rule changes no counts, and registered/declared counts stay equal.

## 6. Stable aliases

- [ ] 6.1 Red→green: the model-facing name is the alias when declared (declarations, nested calls,
      result views), and the canonical `<id>__<local>` remains the identity for grouping, `/extensions`
      and unload; a tool without an alias keeps the canonical name.
- [ ] 6.2 Red→green: collisions refuse the later load naming both owners — alias vs alias, alias vs
      another extension's canonical name, alias vs core name (via `ReservedToolNames` from the
      composition); MCP-tool collisions keep the existing first-composition-wins rule with one
      diagnostic.

## 7. The fitness suite

- [ ] 7.1 A test-host extension (fixtures, not `Lunate.Roslyn`) proves the built-in path end to end:
      manifest with `builtIn`, `tools`, `toolActivation`, `toolAliases`, `workspaceServices`; tools
      declared, aliased, activated by the host; the workspace service acquired/evicted across two
      roots through the extension contract; no collects and no second ALC; `/extensions` reports it
      correctly.
- [ ] 7.2 Docs: `docs/extensions/manifest.md` gains the four-field reference and refusal messages,
      `contributions/services.md` gains per-workspace services, `contributions/tools.md` the alias and
      activation paragraphs; `python3 scripts/docs-sync --check` green.

## 8. Verification

- [ ] 8.1 `openspec validate extension-contract-primitives --strict` passes; final gate:
      `dotnet build Lunate.slnx -c Release`, the touched test projects green in Release,
      `dotnet csharpier check .` (after the last edit); `scripts/verify.sh` is CI's job in this run.
