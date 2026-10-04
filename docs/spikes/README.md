# Phase 0 spikes

Throwaway validation code for the load-bearing assumptions behind Lunate's
architecture (guide card T-03, OpenSpec change `run-phase0-spikes`).

## Rules

- One folder per spike: `S-1` loop (own vs Microsoft Agent Framework harness),
  `S-2` Git Bash/mintty raw-key input, `S-3` startup baselines and budget
  calibration, `S-4` in-process Roslyn loading.
- Each spike contains its throwaway project(s), `report.md` with evidence, and
  produces one proposed ADR under `adr/` (0003+). Adoption is the maintainer's
  sign-off; the spike only proposes.
- Spike projects target `net10.0`, are **not** added to `lunate.sln`, and are
  **not** part of `scripts/verify.sh` (or the CI gates). They inherit
  `Directory.Build.props` but nothing depends on them.
- NuGet packages used here are throwaway and exempt from the "no new packages"
  product rule. No API keys: stubs and recorded streams only.
- Spike code is disposable. When a spike is done, its findings live in
  `report.md` and the ADR; the code can be deleted without ceremony.

## Exclusion from the solution and the gate (task 1.2)

Confirmed 2026-10-04:

- `dotnet sln lunate.sln list` contains only `src/Lunate.*` and
  `tests/Lunate.*.Tests`; no project under `docs/spikes/`.
- `scripts/verify.sh` operates on `lunate.sln` (build, test, format), publishes
  `src/Lunate.Coding/Lunate.Coding.csproj`, and runs `scripts/perf.sh` against
  that binary. It never globs `docs/`.
- The architecture test (`tests/Lunate.Coding.Tests/LayeringTests.cs`) reads
  project files from `src/` only, so spike `.csproj` files cannot violate the
  layering graph.
- `.gitignore` excludes `bin/` and `obj/` at any depth, so spike build output
  stays untracked.

## Status

| Spike | Question | Status | Outcome |
| --- | --- | --- | --- |
| S-1 | Own loop or Microsoft Agent Framework harness? | done 2026-10-04 | Own loop; borrow MAF approvals/session patterns (ADR 0003 proposed) |
| S-2 | Does raw-key reading work in mintty? | automated done 2026-10-04; manual run pending | Probe + headless VT decode green on macOS; ADR 0004 proposed |
| S-3 | Startup baselines and calibrated budgets | done 2026-10-04 | CI 250/300/350 ms, local 150 ms; ADR 0005 proposed |
| S-4 | Does in-process Roslyn load real solutions? | done 2026-10-04 | Go in-process; single-file keeps the MSBuild build host loose (ADR 0006 proposed) |
| S-5 | Rx or plain async for the TUI live area, optionally plus ReactiveUI? | done 2026-10-04 | System.Reactive at the TUI boundary, no ReactiveUI (ADR 0007 proposed) |
