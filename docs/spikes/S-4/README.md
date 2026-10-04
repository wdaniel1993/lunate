# S-4 — MSBuildWorkspace reality check

Question: does `MSBuildLocator` + `MSBuildWorkspace` load a fixture solution
and a large real solution reliably and fast enough, including from the
published single-file build?

Outcome: **go — keep Roslyn in-process** (ADR 0006, proposed). Single-file
releases must ship the MSBuild build host as loose files next to the binary
(or use `IncludeAllContentForSelfExtract`). See [`report.md`](report.md) for
measurements and [`evidence/`](evidence/) for raw runs.

## What is here

| Path | What it is |
| --- | --- |
| `WorkspaceProbe/` | Throwaway console probe (net10.0, outside `lunate.sln`): `selftest`, `locate`, `load <solution> [--runs N] [--file <suffix>] [--edit-iterations N] [--diagnostics]`. |
| `fixture/` | Fixture solution: console app + library + xunit test project. |
| `evidence/` | Raw measurement outputs (fixture, Polly, ReactiveUI, single-file variants). |
| `run.sh` | Re-runs the fixture measurements and single-file publishes into `evidence/`. Large-solution steps are manual (clone is gitignored). |

Pinned packages: `Microsoft.Build.Locator` 1.11.2,
`Microsoft.CodeAnalysis.CSharp.Workspaces` 5.9.0,
`Microsoft.CodeAnalysis.Workspaces.MSBuild` 5.9.0, with
`Microsoft.Build.Framework` 17.11.48 `ExcludeAssets=runtime` (MSBL001: MSBuild
assemblies are loaded from the located SDK, never copied next to the app).

## Reproduce

```bash
bash docs/spikes/S-4/run.sh          # fixture + failure modes + single-file
```

Large-solution runs use gitignored clones under `artifacts/s4-scratch/`; the
exact commits and commands are in [`report.md`](report.md).
