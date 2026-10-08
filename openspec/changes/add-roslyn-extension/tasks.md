# Tasks

## 1. Project + contract (TDD, red-first)

- [ ] 1.1 Packages + csproj rules per design (spike versions, `ExcludeAssets=runtime`, `BuildHost-*` loose); PublicAPI tracking
- [ ] 1.2 `ICSharpBackend` + `WorkspaceLoadResult`/`DiagnosticsResult`/`WorkspaceFailure`/`DiagnosticsScope` per design; PublicAPI entries
- [ ] 1.3 Fixtures `tests/fixtures/solutions/console-app` and `lib-with-tests` (package-free where required for offline tests)

## 2. In-process backend

- [ ] 2.1 Locator init (first call, before any MSBuild type; SDK-missing soft-fail; single init; node-reuse off)
- [ ] 2.2 Workspace manager: discovery rules, cap 2 LRU, dispose on evict, per-instance state
- [ ] 2.3 Restore check (assets file; actionable hint; synthetic-dir unit tests); `WorkspaceFailed` collection + bounded failures; SDK-mismatch message from `RemoteInvocationException`
- [ ] 2.4 Freshness: mtime/length snapshot, apply changed documents without reload, `NotifyFileChanged`, deleted-file reporting; never throw to callers (sweep test)

## 3. cs_diagnostics

- [ ] 3.1 Tool per design (schema, description, annotations read-only, result text + Details, status messages)
- [ ] 3.2 Lazy first-call loading + the no-Roslyn-assemblies-before-first-call test
- [ ] 3.3 Gate: clean → error after edit → reported (file/line) → fixed → clean; warm latency asserted; first-call budget tripwire (10 s CI, disclosed) with dev numbers recorded

## 4. Close

- [ ] 4.1 Layering edges; `dotnet csharpier format .`; `bash scripts/verify.sh` green (incl. de-AT); `openspec validate add-roslyn-extension --type change --strict`; self-review; commit per group
