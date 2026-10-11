# Proposal: slim the windows-latest verify job

## Why

The windows legs are the CI bottleneck (verify 15m39s–18m, pwsh 11m27s–12m24s) and the wall time is not the tests: on Windows, each captured `dotnet test` step sits **silent for 3–4 minutes** after its summary before the next step starts (macOS and ubuntu: zero). That is ~8m of the bash leg and ~3m of the pwsh leg.

## Root cause (probe-verified)

The gate captured test output through `tee`; `tee` ends only at EOF, and EOF needs **every** writer of the pipe to close. Build workers inherit the pipe and outlive `dotnet test` by design, so the step stalls until they exit. Probe snapshots during the stall show exactly two survivors: the waiting `tee` — and **VBCSCompiler**, the Roslyn compiler server (MSBuild node-reuse workers were tried disabled and shaved ~45s; VBCSCompiler alone still held the pass open 3m16s). Redirecting output to a file removes the stall entirely — the wait is on the pipe, not on the runner.

## What changes

- `scripts/verify.sh` + `scripts/verify.ps1`: test-pass output is written to a file and printed when the command ends, instead of being piped — the step ends when its command ends, whatever inherits the pipe. Trade-off: a pass's output appears when it finishes rather than streaming live.
- `global.json`: SDK pin 10.0.103 → 10.0.401 (independent modernization — current supported band; contains dotnet/sdk#52558's MTP hardening, necessary but not sufficient, as the investigation showed).
- Spec: ci-pipeline gains the "captured steps end with their commands" contract; the SDK-pinning requirement gains the band policy.
- No coverage changes: every step stays; no job is dropped; publish proof and budgets still run on every leg.

## Done when

The PR's own windows legs show the stalls gone (bash toward ~8m, pwsh toward ~9m) with all checks green and identical test counts, and the rationale (measurements + probe evidence) is in the PR body.
