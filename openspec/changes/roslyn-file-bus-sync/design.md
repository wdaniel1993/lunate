# Design — roslyn-file-bus-sync

## D1 — Subscription topology: one per session, owned by the services registry

The bus is per session (`ExtensionSession.StartAsync` creates it before extensions load and registers it as `core/file-bus`); the Roslyn backends are per workspace root (T-51's `WorkspaceServices`). The wiring therefore lives at the intersection: `WorkspaceServices` gains `Connect(IFileChangeBus bus)`; each mode calls it right after composing extensions (`_extensions.FileChanges`), so the subscription exists for the whole session. One subscription, `pathPattern: null` — the handler filters explicitly (no glob subtleties, and the payload carries everything needed). Disposal: the subscription is disposed with `WorkspaceServices`; the bus's own `Dispose` drops it regardless. Without extensions there is no bus and no subscription — the stamp scan alone carries sync, exactly today's behavior.

## D2 — Routing: by workspace id, existing entries only, mark-only

The handler maps `FileChangedPayload` → the backend for `payload.WorkspaceId` (the run's `WorktreeRoot`, canonical — the same key the registry uses): `WorkspaceServices.MarkChanged(workspaceId, path)` does a **non-creating** lookup under the services gate and calls the backend's mark path. Absent root → ignored (its first load reads disk; nothing is lost). Evicted/disposed backend → the mark dies with it; the next create reloads from disk. **No backend is ever created by an event** — laziness stays exactly as T-51 pinned it (first `cs_*` request creates).

## D3 — The backend mark path becomes concurrent (the Roslyn-layer work)

The bus invokes handlers on its dispatch queue under a timeout (`HandlerTimeout`; a slow handler is reported and delivery continues). `RoslynBackend.NotifyFileChanged` currently takes `_gate.Wait()` — a mark arriving during a cold load or a long compile would block the handler and could time out. Change: `_dirty` becomes a `ConcurrentDictionary<string, byte>` (with `PathIdentity.Comparer`); `NotifyFileChanged` drops the gate (mark-only, no allocation-heavy work); `SyncChangedDocuments` drains via `TryRemove` (parity: marks for non-document paths linger, as with the `HashSet` today; the set stays bounded by session edits). The workspace gate keeps guarding everything that touches the solution — marks are inputs to the refresh, not mutations of it.

## D4 — Project/solution/build files mark a reload

`.csproj`, `.sln`, `.slnx`, `Directory.Build.props`, `Directory.Build.targets`, `Directory.Packages.props` and `global.json` events set a `ReloadRequested` flag on the entry; the next call on that root disposes the loaded solution and reloads before reporting (the load-count is observable for tests). Rationale: a changed project or build-configuration file invalidates the whole compilation — refreshing documents is not enough, and today the workspace stays stale until eviction. Marks arriving before a load are naturally irrelevant (the pending load reads disk). After a reload with new package references, the reload path reuses the ADR-0006 restore check so a missing/stale restore surfaces the restore hint (pinned by a test; the check is extended only if the test proves a gap). (Q2.)

## D5 — The stamp scan stays; the honest detection rule (Q1)

The bus's documented limit (sign-off D9 of `wire-file-change-bus`): `bash` mutations are **not** published. The content-stamp scan is what keeps out-of-band changes (bash, external editors) visible to `cs_diagnostics` today; removing it would silently regress that class. The final rule, stated honestly in the spec: **tool edits are exact via the bus** (a tool edit is detected even when its content stamp is unchanged); **bash/external edits are caught by the stamp scan**; a **same-stamp change made outside the tool path is seen only at the next reload** (the scan cannot distinguish it — the residual limit). **Bus mark and stamp scan on the same file are idempotent**: a file flagged by both paths refreshes once — one compile, one changed-file count (the drain already merges the two sources; a test pins it). Optional follow-up filed as its own card: after `bash` in a C# workspace, mark changed files exactly via `git status`.

## D6 — Race safety

- **Mark before any load**: ignored (no entry) or queued in `_dirty` (entry exists, not loaded) — both correct; the load reads disk and the refresh drains marks.
- **Mark during a load**: queued in `_dirty` and applied after the load completes — **no edit is lost**; the post-load refresh drains it (pinned by a test).
- **Mark during eviction/dispose**: the services gate serializes lookup vs eviction; the worst case is a mark landing on a just-disposed backend — harmless (state dies; next create reloads).
- **Handler speed**: mark-only path, no backend gate, no disk I/O beyond canonicalization's symlink stats — completes in microseconds, far under the bus timeout; delivery order stays the bus's per-workspace order.
- **Ordering vs the mutation queue**: `write`/`edit` publish after the mutation completes (bus contract), so a mark is never delivered before the new content is on disk.
- **Session end**: the subscription is disposed with `WorkspaceServices` (the bus's own `Dispose` drops it regardless).

## D7 — Proofs

- **Unit (Coding.Tests, fake backends)**: `Connect` + a published `.cs` event marks the right root's backend; an event for an unknown root creates nothing; a non-`.cs` path is ignored; `.csproj`/`.sln`/`.slnx` set the reload flag; after `WorkspaceServices.Dispose` events are no-ops; `.cs` suffix check is case-insensitive.
- **Unit (Roslyn.Tests)**: `NotifyFileChanged` is lock-free (a mark completes while the gate is held — e.g. during a load); `TryRemove` drain parity with the old semantics; reload flag → next call reloads (load count).
- **Integration (real Roslyn, Coding.Tests)**: fixture solution loaded via `cs_diagnostics`; a file rewritten with **different content but an unchanged content stamp** (the gate-test trick: same length, timestamp reset) delivered through a real `FileChangeBus` → the next `cs_diagnostics` reports the new state **and the load count is unchanged** (no reload). A project-file change through the bus → next call reloads once, then reports; **`Directory.Build.props` change → reload**; a reload after a package-reference change surfaces the restore hint when restore output is missing/stale. **An edit during a load is not lost** (mark while the load is in flight → post-load diagnostics see it). **Bus mark + stamp scan idempotent** (a file flagged by both → one changed-file count).
- **Session e2e**: a composed session (store-installed extension so the bus exists) with the real fixture workspace: a scripted `write` tool call then `cs_diagnostics` — fresh output, load count 1.

## D8 — Delta, docs, non-goals

- Delta: **agent-tools** MODIFIED ("C# diagnostics tool") — the definition of "files changed since the last check" (bus marks in-session; stamps out-of-band; project files reload) + two scenarios (bus-delivered stamp-invisible edit; project-file reload). Original scenarios preserved verbatim in order.
- Docs: `docs/spec/extensibility.md` "File change bus" section gains one sentence (the Roslyn backend is the first core subscriber). No guide change.
- No ADR (existing seams); no new packages. Non-goals: watching the filesystem (no FileSystemWatcher), publishing `bash` mutations (bus limit, unchanged), per-extension Roslyn APIs.

## Sign-off (2026-10-14)

Sign-off on Q1–Q5 **with additions** (recorded in the sign-off card `t_7287ffeb`; D1–D8 above fold them):

- **Q1:** keep the content-stamp scan as the backstop; the spec states the honest rule — tool edits exact via the bus, bash/external edits via the stamp scan, a same-stamp out-of-band change seen only at the next reload; **bus mark + scan on the same file are idempotent (one compile)**. Optional follow-up card filed: after `bash` in a C# workspace, mark changed files exactly via `git status`.
- **Q2:** include; extend the trigger list to `Directory.Build.props/.targets`, `Directory.Packages.props`, `global.json`; after a reload with new package references the ADR-0006 restore check surfaces the restore hint.
- **Q3:** yes; marks for a workspace still loading are queued and applied after the load completes (no lost edits); unsubscribe at session end.
- **Q4:** yes; the docs sentence states the honest rule from Q1.
- **Q5:** yes, plus tests — event during load not lost; bus + scan idempotent; `Directory.Build.props` change triggers a reload.
