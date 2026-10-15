# Tasks — roslyn-file-bus-sync

## 1. Roslyn backend

- [ ] 1.1 `_dirty` → `ConcurrentDictionary<string, byte>` (`PathIdentity.Comparer`); `NotifyFileChanged` mark-only (no `_gate`); `SyncChangedDocuments` drains via `TryRemove` (parity with the old semantics).
- [ ] 1.2 `ReloadRequested` on the entry; the next call on the root disposes the loaded solution and reloads before reporting; load count observable for tests.
- [ ] 1.3 Roslyn.Tests: mark completes while the gate is held (lock-free); drain parity; reload flag → next call reloads once; existing suites green.

## 2. Coding wiring

- [ ] 2.1 `WorkspaceServices.Connect(IFileChangeBus)` (one subscription, `pathPattern: null`), handler → `MarkChanged(workspaceId, path)`: non-creating lookup under the services gate; `.cs` (case-insensitive suffix) → backend mark; `.csproj`/`.sln`/`.slnx` → reload mark; everything else ignored; disposal with the services.
- [ ] 2.2 Wire in the three modes (`InteractiveSession`, `PrintMode`, `AcpMode`) right after extensions compose; no extensions → no subscription (scan-only, unchanged).
- [ ] 2.3 Coding.Tests (fakes): routing to the right root; unknown root creates nothing; non-`.cs` ignored; project files set the reload flag; disposed services → no-op; suffix case-insensitivity.

## 3. Integration proofs

- [ ] 3.1 Real-Roslyn: fixture solution + real `FileChangeBus` → a rewrite with an **unchanged content stamp** (same length, timestamp reset) refreshes `cs_diagnostics` with **load count unchanged** (no reload).
- [ ] 3.2 Real-Roslyn: a project-file change through the bus → the next call reloads once, then reports.
- [ ] 3.3 Session e2e: composed session (store extension so the bus exists) + scripted `write` → `cs_diagnostics` fresh, load count 1.

## 4. Docs + delta

- [ ] 4.1 `docs/spec/extensibility.md` "File change bus" section: one sentence (the Roslyn backend is the first core subscriber).
- [ ] 4.2 Delta validates strict; `openspec validate --all --strict`.

## 5. Verify, review, archive

- [ ] 5.1 `scripts/verify.sh` green (both cultures); csharpier/style/analyzers/markdownlint clean.
- [ ] 5.2 Adversarial review; fix findings; focused re-review.
- [ ] 5.3 Archive; push; PR.
