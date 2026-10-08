# Design: add-roslyn-extension

## Context

ADR-0014 fixes the shape (name-based ops; exact signatures here; position lookup in the backend; backend never mutates the workspace). ADR-0006 fixes operations (build host on disk for single-file — release packaging is T-32, but the csproj keeps `BuildHost-*` loose from day one; restore precondition; partial loads surfaced; SDK mismatch soft). S-4 pins versions and numbers (fixtures ~0.65 s cold; warm ~2 ms). No new ADR.

## Packages and csproj

- `Microsoft.CodeAnalysis.CSharp.Workspaces` 5.9.0, `Microsoft.CodeAnalysis.Workspaces.MSBuild` 5.9.0, `Microsoft.Build.Locator` 1.11.2; `Microsoft.Build.Framework` 17.11.48 with `ExcludeAssets="runtime"` + `PrivateAssets="all"` (ADR-0006; silence MSBL001 via the documented pattern).
- `BuildHost-*` publish items kept loose (`ExcludeFromSingleFile`) in the project, so T-32's release inherits it.
- Roslyn gains PublicAPI tracking entries (it is one of the six tracked projects).

## `ICSharpBackend` (signatures pinned here)

```csharp
public interface ICSharpBackend
{
    ValueTask<WorkspaceLoadResult> LoadAsync(CancellationToken ct);
    ValueTask<DiagnosticsResult> GetDiagnosticsAsync(DiagnosticsScope scope, CancellationToken ct);
    void NotifyFileChanged(string absolutePath);
}
```

- `LoadAsync`: uses the run/worktree root the backend is constructed with; discovers the solution; returns status + message; idempotent (already loaded → same result).
- `DiagnosticsScope`: `ChangedFiles` (default) — files modified since the last successful `GetDiagnosticsAsync` (or since load); `Solution` — every document. No file lists in T-25 (the tool passes no paths).
- `WorkspaceLoadResult(Status, Message, SolutionPath?, ProjectCount, DocumentCount, IReadOnlyList<WorkspaceFailure> Failures)`; `Status` = `Loaded | Partial | RestoreRequired | NoSdk | NoSolution`; `WorkspaceFailure(Project?, Message)` bounded (≤20) with a total count. Messages are actionable (ADR-0006 wording: restore command with the path, required SDK version when parseable, unsupported-workload hints).
- `DiagnosticsResult(Items, ErrorCount, WarningCount, Truncated, ScopeDescription)`; item = `(File, Line, Column, Severity, Id, Message)` with 1-based positions, file relative to the root when under it; capped at 200 items (Truncated flag).

## Backend mechanics

- **Init (first call only)**: `MSBuildLocator.RegisterDefaults()` runs before any Microsoft.Build type is touched; `InvalidOperationException`/no SDK → `NoSdk` result with an actionable message; a second init attempt returns the cached result. Set `MSBUILDDISABLENODEREUSE=1` for our build host processes before first use (S-4 hygiene; prevents lingering nodes).
- **Workspace manager**: keyed by canonical solution path; discovery scans the worktree root then one level down for `*.sln`/`*.slnx`; several candidates → pick the one whose name matches the root directory name, else `NoSolution` listing candidates; none → `NoSolution`. **Cap 2 workspaces, LRU** (extensibility spec); evicting disposes the workspace; a later load re-creates it. All state per backend instance; instances are cheap.
- **Restore check**: before compiling, every loaded project must have `obj/project.assets.json` next to its csproj; otherwise `RestoreRequired` with the `dotnet restore` hint per project directory — never the S-4 avalanche. Checker logic unit-tested with synthetic directories (no network); integration fixtures stay package-free so `restore` is trivial/offline.
- **Partial loads**: `WorkspaceFailed` events are collected (bounded) and surfaced; a failed project never invalidates the solution. `RemoteInvocationException` naming a required SDK → message "solution pins SDK X, not installed" + `Partial`/`NoSdk` as appropriate. Never a raw exception to callers — every path returns a result.
- **Freshness**: before each `GetDiagnosticsAsync`, compare `(mtime, length)` against the snapshot per document; changed → re-read and apply via `TryApplyChanges` (fallback: reload just that document's text); `NotifyFileChanged(path)` marks dirty immediately (the FileChanged bus will call this when the CLI host exists; tests call it directly). Deleted files: reported as a failure item for the affected document, workspace kept.
- **No assembly touch before first call**: the tool/backend type is only constructed inside the first invocation (factory + `Lazy<ICSharpBackend>` held by the tool); a test asserts `AppDomain.CurrentDomain.GetAssemblies()` contains no `Microsoft.CodeAnalysis.*`/`Microsoft.Build.*` before the first call and does after.

## `cs_diagnostics` tool

- Name `cs_diagnostics`; annotations: `read-only`; risk derived (read-only). Schema: `scope` (string enum `changed`|`solution`, default `changed`, description explains). Description: "Compiler errors and warnings for files changed since the last check (or the whole solution) — fast compile check after edits without a full dotnet build".
- Result text: counts + up to 10 inline items; full list in `Details` (UI-only). Statuses map to clear text (`NoSdk`, `RestoreRequired`, `NoSolution`, `Partial` include the actionable message).
- The backend is resolved lazily; construction of `RoslynBackend` happens on first execution. (Host-side "tools are not offered without an SDK" is a CLI concern; the tool itself degrades with a clear message — documented.)

## Fixtures and tests

- `tests/fixtures/solutions/console-app/` — package-free console app (primary gate; loads without restore offline). `tests/fixtures/solutions/lib-with-tests/` — library + test project (guide requirement; kept package-free where possible so no network is needed).
- Gate test: load fixture copy (temp dir) → clean diagnostics → introduce a compile error (write the file directly; `edit`-tool path covered by the change's integration note) → `NotifyFileChanged`/mtime → diagnostics report the error at the right file/line → fix → clean again. Warm cycle asserted fast.
- Budget: first call (startup→load→first diagnostics) measured; dev median recorded in the test output; CI tripwire **10 s** (generous, disclosed; S-4 dev baseline ~0.65 s) — a regression tripwire, not a target. Re-calibrate per repo policy if runners migrate.
- LRU cap (3 solutions → oldest evicted), discovery rules, restore-check unit tests, SDK-missing path (simulated by a backend with a forced locator failure injection point), freshness matrix, no-raw-exception sweep, both cultures.

## Layering

`Lunate.Roslyn -> Lunate.Agent` (ITool/ToolOutput/ToolContext); `Lunate.Roslyn.Tests -> {Lunate.Roslyn, Lunate.Agent}`. LayeringChecker learns the edges. No other upward edges.

## Out of scope

`cs_find_symbol` (T-26), `cs_find_references`/`cs_outline`/`cs_rename` (T-31), LSP backend, child-process move, release packaging (T-32 inherits the csproj items), the host offering/hiding logic, the FileChanged bus wiring (host cards).
