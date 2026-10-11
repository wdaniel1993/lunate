# Design: slim the windows-latest verify job

## Measurements (the evidence)

Per-step windows wall time, PR #54 job 114415862515 (bash leg) and 114415862622 (pwsh leg), from step timestamps:

| Step | windows bash | windows pwsh | macos (ref) | ubuntu (ref) |
| --- | --- | --- | --- | --- |
| test (exec) | 1m31 | 2m20 | 2m06 | 1m56 |
| **silent stall after test** | **3m57** | **~3m22** | **0** | **0** |
| test de-AT (exec) | 1m27 | n/a | 2m03 | 1m52 |
| **silent stall after de-AT** | **3m59** | n/a | **0** | **0** |

The stalls are silent, Windows-only, and stable across runs (3m02–3m59, PRs #39–#55); they account for ~8m of the bash leg and ~3m of the pwsh leg.

## Root cause (probe-verified)

`tee` ends only at EOF, and EOF requires every writer of the pipe to close. `dotnet test` spawns build workers that inherit the stdout pipe and outlive the command by design, so the captured step stalls until they exit. Probe evidence (throwaway `probe/windows-test-exit` branch; `verify.sh` run in the background with 20 s process-tree snapshots):

- The stall reproduces with `verify.sh` and with a bare `dotnet test … | tee` pipeline; it does **not** reproduce with output redirected to a file — the same command exits seconds after its summary.
- Snapshots during the stall show the waiting `tee` and, as the only surviving worker, **VBCSCompiler** (the Roslyn compiler server; started during the build, kept alive by its idle timeout, inheriting the pipe). MSBuild node-reuse workers were also present in the first probe; disabling node reuse and the MSBuild server shaved ~45s but left VBCSCompiler holding the pass open 3m16s — a partial mitigation, reverted.
- The standalone `build` step never stalls: it is not piped.

**First hypothesis (investigated, insufficient):** the SDK band. dotnet/sdk#52558 caps the MTP runner's output-consumption wait at 5 s after process exit; it is present in 10.0.401 and absent in our old 10.0.103 pin (both verified by IL inspection). Bumping the pin did **not** remove the stall — the wait is on the pipe's writers, not the runner's own output drain. The bump is kept as an independent modernization.

## The fix

Both gate scripts capture test-pass output to a file and print it when the command ends — no pipe, so no EOF dependency: the step ends when its command ends, regardless of which worker inherits the output. The bash side (`run_tests`) uses a plain shell redirect; PowerShell needs `Start-Process` with `-RedirectStandardOutput`/`-RedirectStandardError`, because its `*>` redirection still captures through a pipeline (measured: the pwsh test step kept its ~3m40 stall with `*>`, gone after the switch). Trade-off: a pass's output appears when the pass finishes rather than streaming live. Verified green on the PR's own CI with the file capture (all four jobs; 1858 tests both passes; windows bash 16m45 → 11m17).

## Pins

- Fix lives in `scripts/verify.sh` + `scripts/verify.ps1` (single source for CI and local gates; no workflow-level knobs to drift).
- The node-reuse / MSBuild-server env vars are **not** kept: they were a partial mitigation and the file capture makes them unnecessary; the comment in `run_tests` records the mechanism.
- The SDK bump to 10.0.401 stays in this change (separate justification above); `rollForward: latestFeature` and the MTP runner setting unchanged.
- Coverage unchanged: all steps, all jobs, the de-AT pass, budgets, publish proof; the zero-test guard parses the same captured file.
- The PR's own windows legs are the before/after measurement.

## Deviations

- The stall's exact duration is set by the surviving workers' idle timeouts (observed 3m02–3m59); the fix removes the wait entirely rather than tuning a timeout.

## Seams

- If a captured step ever stalls again, check for pipe-inheriting survivors first (snapshot recipe in the probe branch's history; the branch is deleted after this change merges).
- Restoring live streaming would need a pipe whose EOF the step does not depend on (e.g. a follow-that-stops-when-the-command-exits); not worth the portability cost now.
