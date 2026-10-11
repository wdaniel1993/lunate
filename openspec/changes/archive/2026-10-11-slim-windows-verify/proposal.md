# Proposal: slim the windows-latest verify job

## Why

The windows legs are the CI bottleneck (verify 15m39s–16m45s, pwsh 11m27s–12m10s) and the wall time is not the tests: on Windows, each captured `dotnet test` step sits **silent for 3–4 minutes** after its summary before the next step starts (macOS and ubuntu: zero). That is ~8m of the bash leg and ~3m22 of the pwsh leg.

## Root cause (probe-verified)

The gate captures test output through `tee`; `tee` ends at EOF, and EOF requires every writer of the pipe to close. MSBuild's **node-reuse workers** and the **MSBuild server** — spawned during evaluation/build — inherit the pipe and outlive `dotnet test` by design, so the step stalls until they time out. A probe run (background `verify.sh`, 20 s process-tree snapshots) shows, during the stall: `tee` alive, no test processes, but `dotnet.exe … MSBuild.dll` workers and `VBCSCompiler.exe` holding the inherited pipe. Redirecting output to a file removes the stall entirely — the wait is on the pipe, not on the runner.

## What changes

- `scripts/verify.sh` + `scripts/verify.ps1`: disable node reuse and the MSBuild server (`MSBUILDDISABLENODEREUSE=1`, `DOTNET_CLI_USE_MSBUILD_SERVER=0`) so no worker holds a captured step's pipe. No-ops on runners without node reuse; nothing built or tested changes.
- `global.json`: SDK pin 10.0.103 → 10.0.401 (independent modernization — current supported band; contains dotnet/sdk#52558's MTP hardening, which the investigation showed is necessary but not sufficient).
- Spec: ci-pipeline gains the no-lingering-workers contract; the SDK-pinning requirement gains the band policy.
- No coverage changes: every step stays; no job is dropped; publish proof and budgets still run on every leg.

## Done when

The PR's own windows legs show the stalls gone (bash toward ~8m, pwsh toward ~9m) with all checks green and identical test counts, and the rationale (measurements + probe evidence) is in the PR body.
