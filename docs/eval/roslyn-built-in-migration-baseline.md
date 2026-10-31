# Roslyn built-in migration — pre-migration baseline

This is the frozen pre-migration comparison point for issue #293. Product code and composition were
unchanged while it was captured. The exact five tool results are also frozen in
[`tests/fixtures/roslyn-built-in-migration/results.json`](../../tests/fixtures/roslyn-built-in-migration/results.json).

## Identity

| Item | Baseline |
| --- | --- |
| Product source commit | `27e46c1185b99dd3120978712c76b402bcaf82df` (the worktree's starting main commit) |
| Result fixture/probe commit | `a2320564` |
| Memory probe commit | `bf652802` |
| SDK / runtime | .NET SDK `10.0.402`; self-contained publish/runtime `10.0.13` |
| OS / machine / RID | macOS `26.3.1` (`25D212`), Apple M4, `osx-arm64` |
| Tool-result fixture | `tests/fixtures/solutions/console-app/ConsoleApp.slnx`; one package-free project, offline restored |
| Memory reference | Polly `0cb0aa74d11b25a9c6fe32951a1ec4a3a3d79948`; `Polly.slnx`, 47 Roslyn projects and 3,145 documents |
| Memory edit target | `src/Polly.Core/Retry/RetryStrategyOptions.cs` |

## Results

| Measurement | Samples | Result | Existing limit |
| --- | --- | --- | --- |
| Published startup (`lunate --version`, 3 warmups, 20 runs) | First: floor/median `219/269 ms`; the script's one retry: `157/241 ms` | `157 ms` floor, `241 ms` median; **over budget** | Local floor budget `150 ms` |
| Idle published process tree, waiting in `lunate mcp roslyn` before a C# call | `285`, `384`, `384 MB` | Median `384 MB`; **over gate** | ADR-0009 idle gate `150 MB` (target `100 MB`) |
| Roslyn host process tree, after reference-solution load | `514`, `514`, `513 MB` | Median `514 MB` | ADR-0009 Roslyn gate `750 MB` |
| Roslyn host process tree, after load and ten edit-to-diagnostics cycles | `511`, `512`, `513 MB` | Median `512 MB` | ADR-0009 Roslyn gate `750 MB` |
| C# tools | See frozen JSON fixture | Exactly `cs_diagnostics`, `cs_find_symbol`, `cs_find_references`, `cs_outline`, `cs_rename`; exact `Output` and `IsError` are fixture-asserted | Compatibility contract |

Startup and idle are already over their existing local limits on this machine; these are baseline
measurements, not migration regressions. No budget was changed. The Roslyn load and edit measurements
are within the unchanged `750 MB` reference-workspace limit.

## Decisions taken

- Use the existing package-free `console-app` fixture and its real composed tool registry for the
  frozen outputs. The selected inputs and outputs are committed; later parity runs compare against
  this file rather than regenerating it.
- Use the pinned Polly reference solution already named by the S-4 evidence, rather than a smaller
  sample project, for the process-tree Roslyn measurement. The ignored scratch checkout's
  `global.json` was set from SDK `10.0.401` to the installed `10.0.402`; the Polly source commit was
  not changed.
- `scripts/memory.sh roslyn` is still a TODO. Bypass that placeholder with its existing `idle` sampler
  and an opt-in `Lunate.Coding.Tests` probe that calls the actual composed diagnostics tool, holds the
  process after load or after ten file-change/diagnostics cycles, and restores the edited source.
  The sampler includes the full process tree at 150 ms intervals.
- Preserve all startup and memory limits as-is. Report the two baseline overages explicitly rather
  than treating them as numbers to recalibrate for this migration.

## Reproduction commands

All .NET commands use SDK `10.0.402` with `MSBUILDDISABLENODEREUSE=1` and `-nodeReuse:false`. The
published binary was built with:

```bash
dotnet publish src/Lunate.Coding/Lunate.Coding.csproj -c Release -r osx-arm64 --self-contained true \
  -p:PublishSingleFile=true -p:PublishReadyToRun=true -o artifacts/publish/osx-arm64 --nologo
```

Startup used the repository's 20-run hyperfine method (hyperfine `2.0.0`):

```bash
BUDGET_MS=150 PERF_RUNS=20 PERF_WARMUP=3 \
  PERF_RESULTS=artifacts/roslyn-migration-baseline-startup.json \
  bash scripts/perf.sh artifacts/publish/osx-arm64/lunate
```

The tool-result fixture was captured and then verified with the built test assembly:

```bash
dotnet exec tests/Lunate.Coding.Tests/bin/Release/net10.0/Lunate.Coding.Tests.dll \
  -method Lunate.Coding.Tests.RoslynMigrationBaselineTests.All_five_tool_results_match_the_frozen_migration_baseline \
  -noLogo
```

Idle RSS used the published MCP server with stdin held open for seven seconds; it received no C#
tool call:

```bash
MEMORY_GATE_MB=150 MEMORY_SAMPLES_MIN=5 \
  bash scripts/memory.sh idle --report-only bash -c \
  'sleep 7 | artifacts/publish/osx-arm64/lunate mcp roslyn'
```

The two Roslyn states used the same restored Polly checkout, the opt-in test probe, and three
independent process-tree samples per state:

```bash
LUNATE_ROSLYN_MEMORY_ROOT="$PWD/artifacts/s4-scratch/polly" \
  MEMORY_GATE_MB=750 MEMORY_SAMPLES_MIN=5 \
  bash scripts/memory.sh idle --report-only dotnet exec \
  tests/Lunate.Coding.Tests/bin/Release/net10.0/Lunate.Coding.Tests.dll \
  -method Lunate.Coding.Tests.RoslynMigrationMemoryProbeTests.Hold_after_reference_solution_load -noLogo

LUNATE_ROSLYN_MEMORY_ROOT="$PWD/artifacts/s4-scratch/polly" \
  MEMORY_GATE_MB=750 MEMORY_SAMPLES_MIN=5 \
  bash scripts/memory.sh idle --report-only dotnet exec \
  tests/Lunate.Coding.Tests/bin/Release/net10.0/Lunate.Coding.Tests.dll \
  -method Lunate.Coding.Tests.RoslynMigrationMemoryProbeTests.Hold_after_ten_edit_to_diagnostics_cycles -noLogo
```

The startup command exited `1` because its measured floor exceeded the unchanged local budget; the
measurement and automatic retry both completed. `dotnet test` built the baseline test project but
the MTP handshake ran zero tests in this environment; the test assembly was invoked directly and
the parity assertion passed.

## Migrated results

Measured against the frozen fixture and baseline commands on the same machine. The migrated source
commit is `161b83ee611244caf685d400e089b47264173313`; the single-file binary reports
`0.1.0+161b83ee611244caf685d400e089b47264173313`. Environment: .NET SDK `10.0.402`, runtime
`10.0.13`, macOS `26.3.1` (`25D2128`), Apple Silicon `arm64` / `osx-arm64`. The Polly reference
checkout is unchanged at `0cb0aa74d11b25a9c6fe32951a1ec4a3a3d79948`.

| Measurement | Before migration | Migrated | Existing limit / result |
| --- | --- | --- | --- |
| Published startup (`lunate --version`, 3 warmups, 20 runs) | Retry floor/median `157/241 ms` (first attempt `219/269 ms`) | Run 1: retry `200/263 ms` (first `173/252 ms`); verify repeat: retry `174/228 ms` (first `176/231 ms`) | `150 ms` floor; all retries are over budget |
| Idle published process tree, before a C# call | `285`, `384`, `384 MB`; median `384 MB` | `298`, `289`, `328 MB`; median `298 MB`; `-86 MB` | ADR-0009 gate `150 MB` (target `100 MB`); both medians are over gate |
| Roslyn process tree, after reference-solution load | `514`, `514`, `513 MB`; median `514 MB` | `509`, `517`, `510 MB`; median `510 MB`; `-4 MB` | `750 MB`; within gate |
| Roslyn process tree, after load and ten edit-to-diagnostics cycles | `511`, `512`, `513 MB`; median `512 MB` | `514`, `518`, `512 MB`; median `514 MB`; `+2 MB` | `750 MB`; within gate |
| Five tool declarations and fixture outputs | Frozen fixture | Exact match; `RoslynMigrationBaselineTests.All_five_tool_results_match_the_frozen_migration_baseline` passed | Compatibility contract; pass |
| Built-in workspace cap | Two | Two; lease/cap tests passed | Two; pass |

Both startup runs exceeded the unchanged `150 ms` floor after the documented retry. The run 1 retry
is `+43/+22 ms` versus the baseline retry; the repeat is `+17/-13 ms`. The idle median is also over
its unchanged gate; the baseline already exceeded it. No budget was changed. The Roslyn measurements
remain below `750 MB`, and the manifest cap of two is enforced by the built-in workspace-service
tests. The lazy-loading test, layering/single-edge tests, golden-session tests and end-to-end golden
test all passed without changing a golden.

The startup, idle and tool-parity measurements use the published migrated binary or the frozen
fixture as shown. The Roslyn memory probe uses the same opt-in `RoslynMigrationMemoryProbeTests`
process-tree harness as the baseline: it loads the real Polly solution and calls the real Roslyn
backend, but constructs the test tool harness directly rather than exercising the production
extension lease. Treat those rows as a before/after Roslyn-backend comparison, not a measurement of
the extension adapter's incremental memory. `scripts/memory.sh roslyn` remains a TODO; the recorded
Roslyn numbers come from the baseline's explicit `idle`-sampler workaround, not that placeholder.

### Verification status

The strict change validation, CSharpier check, Release solution build, focused migration/layering/
lazy-loading/compatibility tests, session goldens, and end-to-end golden all passed. The docs profile
of `scripts/verify.sh` also passed. The full profile was attempted with startup enforcement disabled
so it could reach tests; it built and published successfully, then exceeded the 600-second command
limit during the test phase and did not finish. A separate solution-wide `dotnet test` invocation
reported zero tests and handshake failures (exit `5`) for 22 modules, matching the MTP discovery
limitation recorded for the baseline. No golden diff was observed in the focused golden/e2e runs.

### Migrated reproduction commands

The binary was published from the migrated source commit with the same single-file settings:

```bash
dotnet publish src/Lunate.Coding/Lunate.Coding.csproj -c Release -r osx-arm64 --self-contained true \
  -p:PublishSingleFile=true -p:PublishReadyToRun=true -o artifacts/publish/osx-arm64 --nologo
```

Startup used the same `scripts/perf.sh` method, including its automatic retry:

```bash
BUDGET_MS=150 PERF_RUNS=20 PERF_WARMUP=3 \
  PERF_RESULTS=artifacts/roslyn-migration-migrated-startup.json \
  bash scripts/perf.sh artifacts/publish/osx-arm64/lunate
```

The idle published process tree used three independent seven-second windows:

```bash
MEMORY_GATE_MB=150 MEMORY_SAMPLES_MIN=5 \
  bash scripts/memory.sh idle --report-only bash -c \
  'sleep 7 | artifacts/publish/osx-arm64/lunate mcp roslyn'
```

Each Roslyn state used the same Polly checkout and probe as the baseline, three independent samples
per state:

```bash
LUNATE_ROSLYN_MEMORY_ROOT="$PWD/artifacts/s4-scratch/polly" \
  MEMORY_GATE_MB=750 MEMORY_SAMPLES_MIN=5 \
  bash scripts/memory.sh idle --report-only dotnet exec \
  tests/Lunate.Coding.Tests/bin/Release/net10.0/Lunate.Coding.Tests.dll \
  -method Lunate.Coding.Tests.RoslynMigrationMemoryProbeTests.Hold_after_reference_solution_load -noLogo

LUNATE_ROSLYN_MEMORY_ROOT="$PWD/artifacts/s4-scratch/polly" \
  MEMORY_GATE_MB=750 MEMORY_SAMPLES_MIN=5 \
  bash scripts/memory.sh idle --report-only dotnet exec \
  tests/Lunate.Coding.Tests/bin/Release/net10.0/Lunate.Coding.Tests.dll \
  -method Lunate.Coding.Tests.RoslynMigrationMemoryProbeTests.Hold_after_ten_edit_to_diagnostics_cycles -noLogo
```

The frozen result fixture was verified directly with the built test assembly:

```bash
tests/Lunate.Coding.Tests/bin/Release/net10.0/Lunate.Coding.Tests \
  -method Lunate.Coding.Tests.RoslynMigrationBaselineTests.All_five_tool_results_match_the_frozen_migration_baseline \
  -noLogo
```
