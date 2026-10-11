# Design: slim the windows-latest verify job

## Measurements (the evidence)

Per-step windows wall time, PR #54 job 114415862515 (bash leg) and 114415862622 (pwsh leg), from step timestamps:

| Step | windows bash | windows pwsh | macos (ref) | ubuntu (ref) |
| --- | --- | --- | --- | --- |
| test (exec) | 1m31 | 2m20 | 2m06 | 1m56 |
| **silent stall after test** | **3m57** | **~3m22** | **0** | **0** |
| test de-AT (exec) | 1m27 | n/a | 2m03 | 1m52 |
| **silent stall after de-AT** | **3m59** | n/a | **0** | **0** |

The stalls are silent, Windows-only, and stable across runs (3m04–3m59, PRs #39–#55); they account for ~8m of the bash leg and ~3m22 of the pwsh leg. The per-pass wall is a near-constant ~5m35 regardless of test duration — the stall absorbs the difference.

## Root cause (probe-verified)

The gate's test steps capture output through `tee`; `tee` ends at EOF, and EOF needs **every** writer of the pipe to close. `dotnet test` spawns MSBuild **node-reuse workers** and the **MSBuild server** during evaluation/build; those workers inherit the stdout pipe and, by design, outlive the command. After `dotnet test` exits, the workers keep the pipe open and the step stalls until they time out — minutes per pass.

Evidence (throwaway `probe/windows-test-exit` branch; `verify.sh` run in the background with 20 s process-tree snapshots):

- The stall reproduces with `verify.sh` and with a bare `dotnet test … | tee` pipeline; it does **not** reproduce with output redirected to a file (no pipe → nothing to wait for) — the same command exits ~4 s after the summary.
- Snapshots during the stall show: `tee` alive, **no** `dotnet test` process, **no** test apps — but three `dotnet.exe … MSBuild.dll` worker nodes and `VBCSCompiler.exe` alive, holding the inherited pipe.
- The standalone `build` step never stalls: it is not piped.

**First hypothesis (investigated, insufficient):** the SDK band. dotnet/sdk#52558 caps the MTP runner's output-consumption wait at 5 s after process exit; it is present in 10.0.401 and absent in our old 10.0.103 pin (both verified by IL inspection). Bumping the pin did **not** remove the stall — the wait is on the pipe's writers, not the runner's own output drain. The bump is kept as an independent modernization: current supported band, contains the upstream hardening.

## The fix

Both gate scripts disable node reuse and the MSBuild server for the whole run (`MSBUILDDISABLENODEREUSE=1`, `DOTNET_CLI_USE_MSBUILD_SERVER=0`) so no worker inherits a captured step's pipe. Probe-verified: with the fix, `verify.sh` on Windows completes green (1858 tests, 0 failed, both passes) and the test steps end with their test runs. The settings are no-ops where node reuse is absent (macOS, Linux CI) and change nothing about what is built or tested — only who lingers afterwards.

## Pins

- Fix lives in `scripts/verify.sh` + `scripts/verify.ps1` (single source for CI and local gates; no workflow-level knobs to drift).
- The SDK bump to 10.0.401 stays in this change (separate justification above); `rollForward: latestFeature` and the MTP runner setting unchanged.
- Coverage unchanged: all steps, all jobs, the de-AT pass, budgets, publish proof.
- The PR's own windows legs are the before/after measurement.

## Deviations

- The stall's exact duration is set by the workers' idle timeouts (observed 3m04–3m59); the fix removes the wait entirely rather than tuning a timeout.

## Seams

- If a future captured step stalls again, check for new pipe-inheriting survivors first (the snapshot recipe is in the probe branch's history; the branch is deleted after use).
- The probe branch (`probe/windows-test-exit`) is throwaway and deleted after this change merges.
