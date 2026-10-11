# Design: slim the windows-latest verify job

## Measurements (the evidence)

Per-step windows wall time, PR #54 job 114415862515 (bash leg) and 114415862622 (pwsh leg), extracted from step timestamps:

| Step | windows bash | windows pwsh | macos (ref) | ubuntu (ref) |
| --- | --- | --- | --- | --- |
| test (exec) | 1m31 | 2m20 | 2m06 | 1m56 |
| **gap after test summary** | **3m57** | **~3m22** | **0** | **0** |
| test de-AT (exec) | 1m27 | n/a | 2m03 | 1m52 |
| **gap after de-AT summary** | **3m59** | n/a | **0** | **0** |
| publish | 1m44 | 2m28 | 1m52 | 1m58 |
| build | 44s | 59s | 29s | 36s |
| format + docs lint | 64s | 75s | 55s | 77s |
| everything else | <30s | <30s | <30s | <30s |

The gaps are silent (no log lines between the MTP summary and the next step), Windows-only, and consistent across runs (3m06–3m59 across PRs #53/#54). They account for ~8m of the 15m39s bash leg and ~3m22s of the 12m10s pwsh leg.

## Root cause

dotnet/sdk PR #52558 ("Fix dotnet test hang when the test app process exits but a child process of it remains alive"): the MTP runner (`dotnet test`) consumed the test app's stdout/stderr and kept waiting while a child process holding inherited handles lingered. The fix waits for process exit independently of output and caps output consumption at 5s after exit. Merged to `main` 2026-02-11; backported to `release/10.0.3xx` 2026-03-08. Our pin (10.0.103) is the 10.0.1xx band — feature bands never receive each other's fixes.

The exact lingering child is not identified (no Windows box to sample; candidates: MSBuild node-reuse/server processes, or a test-spawned child on Windows only). The upstream fix is deliberately robust to *any* lingering child, which is why the band bump is the right lever rather than hunting the child.

## Pins

- Pin **10.0.401** (latest active .NET 10 SDK, 2026-09-08). If 10.0.401 surfaces unrelated gate breakage (analyzers/formatter drift), fall back to the latest 10.0.3xx — same fix — and record the choice as a deviation.
- `rollForward: latestFeature` and the `test.runner: Microsoft.Testing.Platform` setting stay unchanged.
- Local pre-validation: install 10.0.401 side-by-side (user-local `~/.dotnet`), run the full gate under it before pushing.
- Coverage unchanged: all steps, all jobs, de-AT pass, budgets, publish proof. The PR measures before/after from the PR's own windows legs.

## Deviations

(filled during apply; none yet)

## Seams

- If the gap persists after the bump, the next probe is `MSBUILDDISABLENODEREUSE=1` + `DOTNET_CLI_USE_MSBUILD_SERVER=0` (recorded in the proposal), then hunting the child.
- Future SDK bumps inherit the policy added to the ci-pipeline "SDK pinning" requirement.
