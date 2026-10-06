# 0006 — In-process Roslyn; the MSBuild build host ships beside the single file

- Status: accepted — 2026-10-04 (maintainer sign-off)
- Date: 2026-10-04
- Spike: `docs/spikes/S-4/report.md` (raw evidence under `docs/spikes/S-4/evidence/`)
- Relates to: ADR 0001 (single-file releases), ADR 0002 (layering), ADR 0014 (C# tools behind a backend interface)

## Context

The guide makes in-process Roslyn the C# differentiator: Lunate should offer
semantic C# tools by loading the user's solution with `MSBuildLocator` +
`MSBuildWorkspace` on first use. S-4 measured that assumption on a fixture
solution and on real OSS repos (Polly at `0cb0aa74…`, 21 projects / 47 Roslyn
projects; ReactiveUI at `88312688…`, 75 projects / 308 Roslyn projects).

Results: loading is fast enough (fixture cold load ~0.64 s; Polly ~3.3 s;
warm edit-to-diagnostics ~2 ms fixture / 90–110 ms Polly) and reliable when
the solution restores. But three real constraints emerged:

1. `MSBuildWorkspace` runs project evaluation in an out-of-process **build
   host** (`BuildHost-netcore/…BuildHost.dll`). A plain `PublishSingleFile`
   bundle embeds that DLL, so the child process cannot start and every load
   aborts with "The build host could not be found". Keeping the build host
   loose (248 KB + 1.6 MB directories) restores full parity with the
   framework-dependent build; `IncludeAllContentForSelfExtract` also works at
   the cost of a one-time ~1 s extraction of ~100 MB.
2. The build host resolves the solution's `global.json` independently. With a
   pinned-but-not-installed SDK, loading aborts (`RemoteInvocationException`,
   `hostfxr_resolve_sdk2`); without restore, workspaces still load but
   diagnostics explode (~17 000 errors on sampled projects); unsupported
   workloads/TFMs yield partial loads with `WorkspaceFailed` diagnostics.
3. Multi-targeting multiplies Roslyn projects (21 `.csproj` → 47 projects,
   75 → 308), and `.slnx` is now common (both repos above, Spectre.Console).

## Decision

- **Roslyn stays in-process** (`Lunate.Roslyn`, loaded on first use). No
  language-server process: the measured latency does not justify IPC and a
  second lifecycle.
- **Single-file releases keep the Roslyn build host on disk.** Mark the
  `BuildHost-*` publish items `ExcludeFromSingleFile` (spike target) and ship
  the directories next to the binary, or enable
  `IncludeAllContentForSelfExtract`. Preferred: loose files — smaller first-run
  cost, no runtime extraction. This is a documented, bounded exception to
  ADR 0001's "one file".
- **Restore is a precondition, checked explicitly.** Before compiling, verify
  `obj/project.assets.json` exists for the loaded projects; run or ask for
  restore instead of presenting the diagnostics avalanche.
- **Loading is partial by design.** `WorkspaceFailed` diagnostics (unsupported
  workloads/TFMs, broken references, cleared metadata references, SDK
  mismatches) are surfaced to the user and the model; a failed project never
  invalidates the loaded solution.
- **SDK mismatch fails soft.** If the build host cannot find the SDK pinned by
  the solution's `global.json`, report it with the required version and
  continue with what loaded (or abort with a diagnostic, never a raw
  exception).
- **MSBuild assemblies are never copied next to the app**
  (`Microsoft.Build.Framework` with `ExcludeAssets="runtime"`,
  `PrivateAssets="all"` — the S-4 probe also pins 17.11.48 because
  `Microsoft.CodeAnalysis.Workspaces.MSBuild` 5.9.0 depends on it).
- **Both `.sln` and `.slnx` are supported inputs.**

## Alternatives considered

- **Out-of-process Roslyn (language-server style)**: rejected — it solves the
  single-file packaging problem by moving it, at the cost of process
  management, IPC latency and deployment complexity; the spike shows the
  in-process path meets the latency bar.
- **Narrow the C# claim** (single files/scripts only): rejected — the large
  solution loads in ~3.3 s and edits diagnose in ~100 ms; the real failures
  are operational (restore, SDK pinning, unsupported workloads), not
  architectural.
- **Plain `PublishSingleFile` without a workaround**: rejected — every
  solution load aborts.
- **Bundle and self-extract all content**: viable fallback, but pays a
  ~1 s first-run extraction and writes ~100 MB to a temp location; keep as an
  option, not the default.
- **Prefer metadata references** for referenced projects: not evaluated in
  S-4; leave to the implementation change if load cost becomes a problem.

## Consequences

- `Lunate.Coding`/`Lunate.Roslyn` must implement assets checks and partial-load
  reporting before the first C# tool ships.
- Release packaging documentation and CI must verify the loose `BuildHost-*`
  directories are present in all three RID artefacts (S-4 only tested
  `osx-arm64`).
- The startup budget work (ADR 0005) does not include Roslyn load: it is
  lazy, first-use, and measured in seconds; a separate budget for first C#
  tool latency can follow with the real feature.
- `docs/guide.md` and the architecture docs should record the single-file
  build-host exception and the restore precondition.
- Re-open if the Roslyn build host gains an in-process mode for single-file
  apps, or if real repos show load times far outside the measured range.
