# Proposal: slim the windows-latest verify job (T-40-era card)

## Why

The windows legs are the CI bottleneck (verify 15m39s, pwsh 12m10s on PR #54) and the wall time is not the tests: on Windows, each `dotnet test` pass finishes its summary and then sits **silent for 3–4 minutes** before the next step (bash leg: 3m57s + 3m59s; pwsh leg: ~3m22s). macOS and ubuntu show zero such gap. That is ~8 of the windows leg's 15 minutes.

## Root cause (measured)

The dotnet SDK's MTP `dotnet test` runner waits on the test app's stdout/stderr consumption after the app exits; a lingering child process that inherited those handles keeps the wait open (dotnet/sdk#52558). The fix — wait for process exit independently of output, with a 5-second cap — merged to `main` on 2026-02-11 and was backported to **release/10.0.3xx**. Our pin is **10.0.103** (the 10.0.1xx band), which never receives band fixes.

## What changes

- `global.json`: SDK pin **10.0.103 → 10.0.401** (latest active .NET 10 band, contains the fix). `rollForward: latestFeature` and the MTP runner setting stay.
- Spec: the "SDK pinning" requirement gains the policy the incident teaches — the pin tracks a supported feature band, and moves when a newer band carries gate-relevant fixes.
- No coverage changes: every step stays; no job is dropped; the publish proof and startup budget still run on every leg.

## Alternatives (measured/considered)

- **MSBuild node-reuse / server env knobs** (`MSBUILDDISABLENODEREUSE=1`, `DOTNET_CLI_USE_MSBUILD_SERVER=0`): targets a different child class; deferred — if the gap persists after the bump, this is the next probe.
- **ReadyToRun off for CI publish** (~1m44s on windows): rejected for now — the publish step and startup budget must reflect the shipped artifact shape; revisit only if the band fix under-delivers.
- **npm/markdownlint caching** (~20–40s): deferred, small.
- **Dropping the pwsh twin or the de-AT pass**: rejected — coverage is the point of the gate.

## Done when

The PR's own windows legs show the gap gone (windows verify back toward ~8m, pwsh toward ~9m) with all checks green and identical test counts, and the rationale (measurements + upstream reference) is in the PR body.
