# S-4 report — MSBuildWorkspace reality check

- Date: 2026-10-04
- Change: `run-phase0-spikes` (guide card T-03)
- Question: does in-process Roslyn (`MSBuildLocator` + `MSBuildWorkspace`) load
  real solutions reliably and fast enough — also from the published single-file
  build?
- Method: throwaway probe `WorkspaceProbe/` (net10.0, outside `lunate.sln`,
  packages pinned: `Microsoft.Build.Locator` 1.11.2,
  `Microsoft.CodeAnalysis.CSharp.Workspaces` 5.9.0,
  `Microsoft.CodeAnalysis.Workspaces.MSBuild` 5.9.0). Fixture solution
  (console + library + xunit test project) and one large OSS solution (Polly,
  pinned by commit) measured cold load and edit-to-diagnostics latency; four
  single-file publish variants were tested.
- Outcome: **go — keep Roslyn in-process**, with constraints (see
  [Recommendation](#recommendation)). Single-file works only when the MSBuild
  *build host* is left on disk next to the binary (or all content is
  self-extracted).
- ADR draft: [`adr/0006-roslyn-placement.md`](../../../adr/0006-roslyn-placement.md)
  (accepted 2026-10-04).

## Environment

| | |
| --- | --- |
| Machine | Apple Silicon macOS (arm64), local dev machine |
| SDK | .NET 10.0.103; `MSBuildLocator` resolved `.NET Core SDK 10.0.103` (`~/.dotnet/sdk/10.0.103`) |
| Locator output | [`evidence/locate.txt`](evidence/locate.txt) |
| Probe build | Release, `MSBUILDDISABLENODEREUSE=1` |

## Measurements

### Fixture solution (3 projects, 16 documents)

| Measurement | Samples (ms) | Median | Evidence |
| --- | --- | --- | --- |
| Cold load (3 separate processes, incl. diagnostics) | 666.8, 624.2, 644.7 | **644.7 ms** | [`evidence/fixture-load.txt`](evidence/fixture-load.txt) |
| Edit-to-diagnostics, 6 cycles in one process | 505.1, 19.7, 2.3, 2.0, 2.0, 2.0 | **2.2 ms** (warm 2.0 ms) | [`evidence/fixture-edit.txt`](evidence/fixture-edit.txt) |

Zero workspace failures; zero errors. The first edit cycle pays the project's
first compilation (505 ms); every subsequent toggle costs ~2 ms. `Calculator.cs`
was restored after the run.

### Large solution — Polly

- Repo: `https://github.com/App-vNext/Polly.git`, commit
  **`0cb0aa74d11b25a9c6fe32951a1ec4a3a3d79948`** (2026-10-02), shallow clone in
  `artifacts/s4-scratch/polly/` (gitignored, not committed).
- 21 `.csproj`, multi-targeted (`net8.0;net6.0;netstandard2.0;net472;net462`) →
  **47 Roslyn projects, 3145 documents**. `dotnet restore Polly.slnx` clean.
- Restored, `Polly.slnx`:

| Measurement | Samples (ms) | Median | Evidence |
| --- | --- | --- | --- |
| Load, 3 runs in one process | 3537.6, 3041.4, 3323.1 | **3323.1 ms** (process wall 10.0 s / 3 runs) | [`evidence/polly-load.txt`](evidence/polly-load.txt) |
| Edit-to-diagnostics, 4 cycles | 783.8, 105.9, 89.8, 86.3 | **97.8 ms** (warm 89.8 ms) | [`evidence/polly-edit.txt`](evidence/polly-edit.txt) |
| Diagnostics sample (4 projects) | net462/net472/net6.0/netstandard2.0 | 0 errors | [`evidence/polly-diagnostics.txt`](evidence/polly-diagnostics.txt) |

The edit target was `src/Polly.Core/Retry/RetryStrategyOptions.cs`. The first
edit costs ~784 ms; warm edits stay below ~106 ms, dominated by the project's
analyzers/source generators rather than Roslyn itself.

### Large solution — ReactiveUI (stress / unsupported-target case)

- Repo: `https://github.com/reactiveui/ReactiveUI.git`, commit
  **`88312688d0281fd260977dfd78bb12e1c85ffaaa`** (2026-10-04), shallow clone.
- 75 `.csproj` → **308 Roslyn projects, 11 041 documents**. `reactiveui.slnx`
  opened in **15.2 s** with **167 workspace failures** and a partially loaded
  model ([`evidence/reactiveui-slnx-load.txt`](evidence/reactiveui-slnx-load.txt)).
- `dotnet restore` fails outright: the repo targets **net11.0** and the installed
  SDK is 10.0.103 (`NETSDK1045`). Workspace failures also include missing
  Android/iOS/MAUI workloads and missing .NET Framework reference assemblies.
- This is the realistic "unsupported project types" case: loading is
  best-effort, not all-or-nothing.

### Candidate rejected

- Spectre.Console, commit `01027efcd6c40c5ca1b2f0eb22261336c7209318`
  (2026-09-28): after its restructuring it has only **9** projects, below the
  "large" bar. Not loaded.

## Failure modes

| Scenario | What happens | Evidence |
| --- | --- | --- |
| **Missing restore (fixture)** | Solution loads (12 vs 16 documents); the test project reports 8 errors (`CS0246` Xunit missing, `CS5001` no entry point, `CS0103`). Package-free projects are unaffected. No workspace-level failure is emitted. | [`evidence/fixture-norestore.txt`](evidence/fixture-norestore.txt) |
| **Missing restore (Polly)** | 47 projects still "load", 16 workspace failures (net462/net472 reference assemblies), and sampled projects report **~17 000 errors each** (`CS0518`, `CS0246`, …) — a diagnostics avalanche. | [`evidence/polly-norestore.txt`](evidence/polly-norestore.txt) |
| **Broken reference (fixture, `ProjectReference` to a missing project)** | `dotnet restore` exits **0** ("Skipping project … not found"); workspace load reports 2 failures and the project loads with `CS0246`/`CS0103`. | [`evidence/fixture-brokenref.txt`](evidence/fixture-brokenref.txt) |
| **Unsupported project types / workloads** | ReactiveUI's Android/MAUI projects fail to evaluate; the solution still loads 308 projects with 167 failures; one sampled project shows 598 errors. | [`evidence/reactiveui-slnx-load.txt`](evidence/reactiveui-slnx-load.txt) |
| **Solution `global.json` pins an uninstalled SDK** | The **build host** calls `hostfxr_resolve_sdk2` against the solution directory and aborts: `RemoteInvocationException … A compatible .NET SDK was not found. Requested SDK version: 10.0.401` (Polly's `global.json`; only 10.0.103 installed). Patching the scratch clone's `global.json` to the installed SDK fixes it. | [`evidence/polly-load-sdk-mismatch.txt`](evidence/polly-load-sdk-mismatch.txt) |
| **`dotnet run`/build node reuse** | With node reuse on, concurrent builds left MSBuild nodes waiting and `dotnet run` hung; the spike runs use `MSBUILDDISABLENODEREUSE=1` and invoke the built binary directly. Development-environment note, not a library defect. | — |

## Single-file findings

Four Release publishes (`osx-arm64`, framework-dependent,
`PublishSingleFile=true`):

| Variant | Result | Evidence |
| --- | --- | --- |
| Plain single file | **Fails at load**: `System.Exception: The build host could not be found at …/BuildHost-netcore/Microsoft.CodeAnalysis.Workspaces.MSBuild.BuildHost.dll` (exit 134). The build host DLLs are bundled into the single file, so the child process cannot launch. `locate` works; only solution loading breaks. | [`evidence/single-file-locate.txt`](evidence/single-file-locate.txt), [`evidence/single-file-fixture.txt`](evidence/single-file-fixture.txt) |
| Single file + `ResolvedFileToPublish` marked `ExcludeFromSingleFile` for `BuildHost-*` | **Works**: fixture load 631 ms, warm edit ~2 ms; Polly load median **3115.9 ms**, edit median **112.9 ms** — parity with framework-dependent runs. Cost: `BuildHost-netcore/` (248 KB) and `BuildHost-net472/` (1.6 MB) ship as loose files next to the 105 MB binary. | [`evidence/single-file-workaround-fixture.txt`](evidence/single-file-workaround-fixture.txt), [`evidence/single-file-polly.txt`](evidence/single-file-polly.txt) |
| `IncludeAllContentForSelfExtract=true`, build host bundled | **Works**: all files extract to `~/.net/WorkspaceProbe/<hash>/`; first `selftest` run 1.01 s (extraction), later runs 0.02 s; fixture load 635 ms. Cost: first-run extraction of ~100 MB and a temp copy of the app. | [`evidence/single-file-extract.txt`](evidence/single-file-extract.txt) |
| Startup (`selftest`, 3 runs each) | framework-dependent 0.02 s · plain single-file 0.02 s · single-file + loose host 0.02 s. No measurable single-file startup penalty at this size. | [`evidence/startup.txt`](evidence/startup.txt) |

**Important architectural detail:** `MSBuildWorkspace` is not one process.
Compilation and diagnostics run in-process, but MSBuild project evaluation runs
in an out-of-process **Roslyn build host** (`dotnet exec
BuildHost-netcore/…BuildHost.dll`). That child process is what single-file
packaging breaks, and it also resolves the solution's `global.json`
independently of the parent.

## Recommendation

**Go — keep Roslyn in-process** (`Lunate.Roslyn` on first use, as planned).
The measurements clear the bar for a coding agent: ~0.6 s to load a small
solution, ~3.3 s to load 47 real projects, and warm edit-to-diagnostics of
~2 ms (fixture) / 90–110 ms (Polly). Moving Roslyn out of process would add
its own IPC and lifecycle complexity without evidence of need; narrowing the
C# claim is not justified by these numbers.

Operational requirements this evidence forces (all proposed in ADR 0006):

1. **Single-file releases keep the build host on disk.** Mark `BuildHost-*`
   files `ExcludeFromSingleFile` (the spike's csproj has the target) or enable
   `IncludeAllContentForSelfExtract`. Preferred: loose files — small, no
   first-run extraction cost.
2. **Restore is a precondition.** Without `obj/project.assets.json`, load
   "succeeds" and emits tens of thousands of errors. Check assets before
   compiling; run or prompt for restore instead of feeding the model garbage.
3. **Treat load as partial and report it.** `WorkspaceFailed` diagnostics must
   surface to the user/model (unsupported workloads/TFMs, broken references,
   cleared metadata references) — never silently.
4. **Handle SDK mismatch.** The build host honors the project's `global.json`;
   when the pinned SDK is missing, loading aborts with
   `RemoteInvocationException`. Detect and explain, do not crash.
5. **Ship `Microsoft.Build.Framework` `ExcludeAssets=runtime`** (MSBL001) and
   let `MSBuildLocator` load MSBuild from the installed SDK.
6. **Support `.sln` and `.slnx`** — MSBuildWorkspace 5.9 opened both.

## Surprises

- Single-file packaging fails with a clear exception only because the build
  host is a separate process; the parent `MSBuildLocator` works fine in a
  single-file app. The fix is a two-line publish target, not a different
  architecture.
- Workspace loads and compilations are **~50× faster** than the first
  impression suggests: cold load dominates, and the first compilation per
  project is the slow one (505 ms fixture, 784 ms Polly); everything warm is
  milliseconds.
- `dotnet restore` exits 0 on a broken `ProjectReference` (it skips the missing
  project), so restore success is not a reference-integrity signal.
- Multi-targeting multiplies project count (21 `.csproj` → 47 Roslyn projects,
  75 → 308); UI load must not assume 1:1.
- A solution's `global.json` can break loading even though the parent process
  resolved an SDK successfully.
- `.slnx` is now the default in several major repos (Spectre.Console, Polly,
  ReactiveUI) — supporting it is not optional.

## Limits

- One machine and one day: absolute numbers are a snapshot, not a benchmark.
- Polly is desktop-only server/library code; no Blazor, Unity or WebAssembly
  workloads were exercised. ReactiveUI's mobile targets failed to evaluate on
  this machine, which is itself evidence but leaves their success path unknown.
- Single-file runs are `osx-arm64` only; Windows/Linux packaging was not
  re-tested (the build-host mechanism is platform-independent, the publish
  target should be verified on all three).
- The probe measures evaluation + compilation only; it does not exercise
  workspace update storms, analyzer loading, or long-running memory pressure.
- Spike code is throwaway and lives outside `lunate.sln`; nothing here changes
  `src/`.

## Addendum — memory (ADR-0003 revision, 2c, 2026-10-04)

Peak RSS was not measured in the first pass. Method: `mem-sample.sh` samples
the whole process tree every 150 ms (the Roslyn build host is a child process,
so a plain `/usr/bin/time -l` on the parent would undercount); three runs each
on Polly at the pinned commit, `MSBUILDDISABLENODEREUSE=1`.

| Run | Peak RSS, process tree (KB) |
| --- | --- |
| load only | 501,520 · 500,816 · 498,256 → median ≈ 489 MB |
| load + 10 edit cycles | 495,168 · 489,312 · 501,536 → median ≈ 484 MB |

A 47-project workspace costs ≈ 0.5 GB peak RSS, dominated by the load;
10 edit cycles do not measurably add to it. Sampling at 150 ms may miss
sub-150 ms peaks. The startup budgets (ADR 0005) do not cover this — it is
lazy, first-use cost that should be documented in the TUI/README.

- Budget: the Roslyn gate is 750 MB peak RSS (process tree) for a ~50-project
  workspace — these medians × 1.5, rounded up (ADR 0009).
