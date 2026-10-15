# Tasks — roslyn-file-bus-sync

## 1. Roslyn backend

- [x] 1.1 `_dirty` → `ConcurrentDictionary<string, byte>` (`PathIdentity.Comparer`); `NotifyFileChanged` mark-only (no `_gate`); `SyncChangedDocuments` drains via `TryRemove` (parity with the old semantics; bus mark + scan idempotent — one compile, one changed-file count).
- [x] 1.2 `ReloadRequested` on the entry; the next call on the root disposes the loaded solution and reloads before reporting; load count observable for tests; trigger list: `.csproj`, `.sln`, `.slnx`, `Directory.Build.props`, `Directory.Build.targets`, `Directory.Packages.props`, `global.json`.
- [x] 1.3 Roslyn.Tests: mark completes while the gate is held (lock-free); drain parity; reload flag → next call reloads once; existing suites green.
- [x] 1.4 Restore-hint path after a reload with new package references (missing/stale restore output): pin with a test; extend the ADR-0006 check only if the test proves a gap.

## 2. Coding wiring

- [x] 2.1 `WorkspaceServices.Connect(IFileChangeBus)` (one subscription, `pathPattern: null`), handler → `MarkChanged(workspaceId, path)`: non-creating lookup under the services gate; `.cs` (case-insensitive suffix) → backend mark; `.csproj`/`.sln`/`.slnx` → reload mark; everything else ignored; disposal with the services.
- [x] 2.2 Wire in the three modes (`InteractiveSession`, `PrintMode`, `AcpMode`) right after extensions compose; no extensions → no subscription (scan-only, unchanged).
- [x] 2.3 Coding.Tests (fakes): routing to the right root; unknown root creates nothing; non-`.cs` ignored; project files set the reload flag; disposed services → no-op; suffix case-insensitivity.

## 3. Integration proofs

- [x] 3.1 Real-Roslyn: fixture solution + real `FileChangeBus` → a rewrite with an **unchanged content stamp** (same length, timestamp reset) refreshes `cs_diagnostics` with **load count unchanged** (no reload).
- [x] 3.2 Real-Roslyn: a project-file change through the bus → the next call reloads once, then reports; **`Directory.Build.props` change → reload**; reload after a package-reference change surfaces the restore hint when restore output is missing/stale.
- [x] 3.3 Real-Roslyn: **an edit during a load is not lost** (mark while the load is in flight → post-load diagnostics see it); **bus + scan idempotent** (flagged by both → one changed-file count).
- [x] 3.4 Session e2e: composed session (store extension so the bus exists) + scripted `write` → `cs_diagnostics` fresh, load count 1.

## 4. Docs + delta

- [ ] 4.1 `docs/spec/extensibility.md` "File change bus" section: the honest rule (tool edits exact via the bus; bash/external via the stamp scan; same-stamp out-of-band seen at next reload) + the first-core-subscriber sentence.
- [ ] 4.2 Delta validates strict; `openspec validate --all --strict`.

## 5. Verify, review, archive

- [ ] 5.1 `scripts/verify.sh` green (both cultures); csharpier/style/analyzers/markdownlint clean.
- [ ] 5.2 Adversarial review; fix findings; focused re-review.
- [ ] 5.3 Archive; push; PR.
