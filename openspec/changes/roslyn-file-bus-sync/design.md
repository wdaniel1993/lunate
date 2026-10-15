# Design — roslyn-file-bus-sync

## D1 — Subscription topology: one per session, owned by the services registry

The bus is per session (`ExtensionSession.StartAsync` creates it before extensions load and registers it as `core/file-bus`); the Roslyn backends are per workspace root (T-51's `WorkspaceServices`). The wiring therefore lives at the intersection: `WorkspaceServices` gains `Connect(IFileChangeBus bus)`; each mode calls it right after composing extensions (`_extensions.FileChanges`), so the subscription exists for the whole session. One subscription, `pathPattern: null` — the handler filters explicitly (no glob subtleties, and the payload carries everything needed). Disposal: the subscription is disposed with `WorkspaceServices`; the bus's own `Dispose` drops it regardless. Without extensions there is no bus and no subscription — the stamp scan alone carries sync, exactly today's behavior.

## D2 — Routing: by workspace id, existing entries only, mark-only

The handler maps `FileChangedPayload` → the backend for `payload.WorkspaceId` (the run's `WorktreeRoot`, canonical — the same key the registry uses): `WorkspaceServices.MarkChanged(workspaceId, path)` does a **non-creating** lookup under the services gate and calls the backend's mark path. Absent root → ignored (its first load reads disk; nothing is lost). Evicted/disposed backend → the mark dies with it; the next create reloads from disk. **No backend is ever created by an event** — laziness stays exactly as T-51 pinned it (first `cs_*` request creates).

## D3 — The backend mark path becomes concurrent (the Roslyn-layer work)

The bus invokes handlers on its dispatch queue under a timeout (`HandlerTimeout`; a slow handler is reported and delivery continues). `RoslynBackend.NotifyFileChanged` currently takes `_gate.Wait()` — a mark arriving during a cold load or a long compile would block the handler and could time out. Change: `_dirty` becomes a `ConcurrentDictionary<string, byte>` (with `PathIdentity.Comparer`); `NotifyFileChanged` drops the gate (mark-only, no allocation-heavy work); `SyncChangedDocuments` drains via `TryRemove` (parity: marks for non-document paths linger, as with the `HashSet` today; the set stays bounded by session edits). The workspace gate keeps guarding everything that touches the solution — marks are inputs to the refresh, not mutations of it.

## D4 — Project/solution files mark a reload

`.csproj`, `.sln`, `.slnx` events set a `ReloadRequested` flag on the entry; the next call on that root disposes the loaded solution and reloads before reporting (the load-count is observable for tests). Rationale: a changed project file invalidates the whole compilation — refreshing documents is not enough, and today the workspace stays stale until eviction. Marks arriving before a load are naturally irrelevant (the pending load reads disk). (Q2.)

## D5 — The stamp scan stays (Q1)

The bus's documented limit (sign-off D9 of `wire-file-change-bus`): `bash` mutations are **not** published. The content-stamp scan is what keeps out-of-band changes (bash, external editors) visible to `cs_diagnostics` today; removing it would silently regress that class. The subscription's value is the exact, stamp-independent mark for in-session mutations — the gap `CsDiagnosticsGateTests` had to work around by calling `NotifyFileChanged` directly. Design states this honestly: the scan is the backstop, the bus is the precise path; the earlier "instead of polling" intent is satisfied for the tool-edit path (no stamp dependence, exact paths) while out-of-band detection is preserved. If the maintainer prefers full removal, the design notes the regression (bash edits invisible until reload) — not recommended.

## D6 — Race safety

- **Mark before any load**: ignored (no entry) or queued in `_dirty` (entry exists, not loaded) — both correct; the load reads disk and the refresh drains marks.
- **Mark during a load**: queued in `_dirty`; the post-load refresh drains it — the file is re-read once, consistently.
- **Mark during eviction/dispose**: the services gate serializes lookup vs eviction; the worst case is a mark landing on a just-disposed backend — harmless (state dies; next create reloads).
- **Handler speed**: mark-only path, no backend gate, no I/O — completes in microseconds, far under the bus timeout; delivery order stays the bus's per-workspace order.
- **Ordering vs the mutation queue**: `write`/`edit` publish after the mutation completes (bus contract), so a mark is never delivered before the new content is on disk.

## D7 — Proofs

- **Unit (Coding.Tests, fake backends)**: `Connect` + a published `.cs` event marks the right root's backend; an event for an unknown root creates nothing; a non-`.cs` path is ignored; `.csproj`/`.sln`/`.slnx` set the reload flag; after `WorkspaceServices.Dispose` events are no-ops; `.cs` suffix check is case-insensitive.
- **Unit (Roslyn.Tests)**: `NotifyFileChanged` is lock-free (a mark completes while the gate is held — e.g. during a load); `TryRemove` drain parity with the old semantics; reload flag → next call reloads (load count).
- **Integration (real Roslyn, Coding.Tests)**: fixture solution loaded via `cs_diagnostics`; a file rewritten with **different content but an unchanged content stamp** (the gate-test trick: same length, timestamp reset) delivered through a real `FileChangeBus` → the next `cs_diagnostics` reports the new state **and the load count is unchanged** (no reload). A project-file change through the bus → next call reloads once, then reports.
- **Session e2e**: a composed session (store-installed extension so the bus exists) with the real fixture workspace: a scripted `write` tool call then `cs_diagnostics` — fresh output, load count 1.

## D8 — Delta, docs, non-goals

- Delta: **agent-tools** MODIFIED ("C# diagnostics tool") — the definition of "files changed since the last check" (bus marks in-session; stamps out-of-band; project files reload) + two scenarios (bus-delivered stamp-invisible edit; project-file reload). Original scenarios preserved verbatim in order.
- Docs: `docs/spec/extensibility.md` "File change bus" section gains one sentence (the Roslyn backend is the first core subscriber). No guide change.
- No ADR (existing seams); no new packages. Non-goals: watching the filesystem (no FileSystemWatcher), publishing `bash` mutations (bus limit, unchanged), per-extension Roslyn APIs.

## Open Questions (sign-off)

- **Q1 — Scan disposition:** keep the content-stamp scan as the out-of-band backstop (recommended — bash/external edits stay visible; the subscription is the exact path for in-session mutations) or remove it per the earlier "instead of polling" intent (bash edits then invisible until reload — a behavior regression; not recommended)?
- **Q2 — Project files:** include the reload-mark for `.csproj`/`.sln`/`.slnx` (recommended — otherwise a changed project file leaves the workspace stale until eviction) or defer to its own card?
- **Q3 — Topology:** one session subscription owned by `WorkspaceServices` (`Connect` from the three modes), routing by workspace id, **existing entries only** (marks never create backends), `.cs` filter in the handler, `_dirty` → concurrent so the handler never blocks on the backend gate — confirm.
- **Q4 — Scope:** delta = agent-tools only; no extensions-spec change (the bus itself is unchanged); no ADR; one docs sentence — confirm.
- **Q5 — Proofs as listed** (unit routing/laziness; lock-free mark; real-Roslyn stamp-invisible integration with load-count assertion; project-file reload; session e2e) — confirm.
