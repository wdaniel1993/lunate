# S-3 report — startup baselines and calibrated budgets

- Date: 2026-10-04
- Change: `run-phase0-spikes` (guide card T-03)
- Question: what are the startup baselines per CI runner and on the dev
  machine, and what budgets follow with documented headroom?
- Method: the CI matrix is the instrument. Four `gh workflow run ci.yml`
  dispatches (three completed during this calibration; one more context run),
  plus the recent push runs, measured the published single-file binary with
  `hyperfine` (20 runs, 3 warmups) on each GitHub-hosted runner; the dev
  machine was measured with the same script.
- Outcome: CI budgets calibrated to **linux 250 ms, macOS 300 ms, Windows
  350 ms**; local default stays **150 ms**. Raw medians:
  [`evidence/ci-medians.txt`](evidence/ci-medians.txt),
  [`evidence/run-list.json`](evidence/run-list.json).
- ADR draft: [`adr/0005-budget-calibration.md`](../../../adr/0005-budget-calibration.md)
  (proposed — awaiting maintainer sign-off).

## Measurements

All values are the `startup median` printed by `scripts/perf.sh` in the verify
jobs, collected with
`gh run view <id> --log | grep "startup median"`. Raw output is in
`evidence/ci-medians.txt`. Windows runs verify twice (bash and PowerShell), so
it has two medians per run; the per-run value used below is their mean.

| Run | Trigger | Commit | Ubuntu | macOS | Windows |
| --- | --- | --- | --- | --- | --- |
| 37182341685 | push | `5a7e4bc` | 196 | 158 | 222, 229 |
| 37182340018 | push | `b371611` | 183 | 219 | 163, 168 |
| 37182357357 | push | `a4dbed4` | 184 | 181 | 229, 227 |
| 37191807488 | push | `b773b06` | 184 | 166 | 220, 220 |
| 37192902973 | dispatch | `b773b06` | 169 | 165 | 227, 229 |
| 37192907183 | dispatch | `b773b06` | 186 | **380 (budget failure)** | 228, 224 |
| 37193233585 | dispatch | `b773b06` | 151 | 231 | 200, 199 |
| 37193236643 | dispatch | `b773b06` | 134 | **256 (budget failure)** | 232, 252 |
| cited recent green (task prompt) | — | — | 160 | 161 | 220 |

The three dispatches from this session are runs 37192902973, 37193233585 and
37193236643. Two of the eight runs failed the old 250 ms macOS budget on a
single noisy measurement (380 ms and 256 ms) while their other OSes passed —
that is exactly the runner noise the calibration must absorb.

Dev machine (macOS arm64, this checkout, `scripts/perf.sh` at defaults):

| Samples | Median | Evidence |
| --- | --- | --- |
| 95 ms, 95 ms (session), 94–96 ms (cited) | 95 ms | [`evidence/local-perf-run1.json`](evidence/local-perf-run1.json), [`local-perf-run2.txt`](evidence/local-perf-run2.txt) |

## Calibration

Primary sample = the three dispatches above plus the cited recent-green
baselines (the instrument the change prescribes). Budget = median × 1.5,
rounded **up** to the next 50 ms so it never sits below the intended headroom.

| Environment | Samples (ms) | Median | ×1.5 | Budget |
| --- | --- | --- | --- | --- |
| Ubuntu | 169, 151, 134, 160 | 155.5 | 233.25 | **250** |
| macOS | 165, 231, 256, 161 | 198 | 297 | **300** |
| Windows | 228, 199.5, 242, 220 | 224 | 336 | **350** |
| Local dev | 95, 95, 94, 96 | 95 | 142.5 | **150** (unchanged) |

Sensitivity: using all eight CI runs instead of the dispatches would give
Ubuntu 183.5 → 300, macOS 200 → 300, Windows 225.75 → 350. Only the Ubuntu
value is sensitive to whether the slower morning runs are included; the
calibration procedure below is the same either way, and re-calibration can
revisit it if the older runs are considered representative.

The budgets catch the regression the spec cares about — a change that doubles
the median (e.g. Ubuntu 155 → 310, macOS 198 → 396, Windows 224 → 448) trips
every one of them.

## Applied changes

- `.github/workflows/ci.yml` matrix: `budget_ms` 250 / 300 / 350 for
  ubuntu / macos / windows (was 250 / 250 / 300), with the comment updated to
  point at this report and ADR 0005.
- `scripts/perf.sh` local default: stays `BUDGET_MS=150` — 95 ms × 1.5 = 142.5
  rounds to 150, so calibration confirms the existing default. CI overrides it
  per runner through the matrix (`BUDGET_MS` env); `scripts/verify.sh` and
  `scripts/verify.ps1` pass it through unchanged.
- `scripts/gate-tests.sh` still verifies that the gate trips at
  `BUDGET_MS=1` and passes at `BUDGET_MS=999999` (run below).

## Re-calibration procedure

Repeatable, no code changes needed:

1. Dispatch the matrix at least twice and let them finish:
   ```bash
   gh workflow run ci.yml --ref main
   gh run list --workflow ci.yml -L 5 --json databaseId,status,conclusion,headSha
   ```
2. Collect the medians per runner: for each run id,
   `gh run view <id> --log | grep "startup median"` (the job name is the first
   tab-separated field). Save the raw output next to this report.
3. Measure the dev machine: publish with `scripts/verify.sh` (or reuse
   `artifacts/publish/<rid>/lunate`), then run
   `scripts/perf.sh artifacts/publish/<rid>/lunate`.
4. Per environment, take the median of the run medians; multiply by 1.5;
   round **up** to the next 50 ms. A run that failed only its budget check is
   still a sample (that is the noise being measured).
5. Update the `budget_ms` values in `.github/workflows/ci.yml`; update
   `scripts/perf.sh`'s default only if the local value changes. Run
   `bash scripts/gate-tests.sh` and `bash scripts/verify.sh` afterwards.
6. Record the new measurements and the reason (new runner image, hardware,
   baseline shift) in this report and re-check ADR 0005.

## Surprises

- Runner noise dominates: macOS medians on the same commit ranged 165 → 380 ms
  in one afternoon, and two of three dispatches failed the old 250 ms macOS
  budget. The median-based budget still raised macOS to 300, not because the
  typical startup changed but because the tail got measured.
- Windows sits at 200–252 ms with two verify passes per run that track each
  other closely; it is the slowest runner despite being the fastest when the
  morning runs were cooler (163–168 ms).
- Ubuntu trended fast over the day (196 → 134 ms) as caches warmed; early
  values inflate any all-runs median.
- The 50% formula pushed macOS up the most, so the old 250 ms macOS budget was
  the only one that was actively flaky.

## Limits

- One day and eight runs: this is a snapshot of shared GitHub-hosted runners,
  not a controlled benchmark.
- Extreme runner noise (the 380 ms sample) would exceed even the new budgets;
  a single flaky failure is a re-calibration signal, not proof of a
  regression. The spec's "doubling fails" scenario still holds.
- The local baseline is one arm64 macOS machine; `perf.sh`'s 150 ms default is
  deliberately unchanged so slower dev machines are not gated by this
  machine's number.
- Spike code is throwaway and lives outside `lunate.sln`; nothing here changes
  `src/`.

## Addendum — idle-memory decomposition (2026-10-04)

The S-5 baseline's 82.1 MB idle peak RSS against the 100 MB budget prompted a
decomposition: what is the memory made of, and do GC settings help? Method:
`docs/spikes/S-5/mem-probe.sh` publishes minimal single-file ReadyToRun probes
(uncompressed, per ADR-0008) and samples peak RSS of the process tree (150 ms,
3 runs each, macOS arm64). Raw results:
`docs/spikes/S-5/evidence/mem-decomposition.txt`.

| Configuration | Peak RSS (median, MB) |
| --- | --- |
| `hello` (runtime + R2R floor) | ~70.4 |
| `hello` + Spectre.Console | ~80.4 |
| S5.Baseline `--idle`, default env | ~74.9 |
| S5.Baseline + `DOTNET_GCConserveMemory=9` | ~74.8 |
| S5.Baseline + `DOTNET_gcServer=1` | ~76.7 |
| S5.Baseline + `DOTNET_TieredCompilation=0` | ~75.1 |

Findings: the floor is the .NET runtime plus the ReadyToRun image (~70 MB for
a hello-world single-file R2R app on this machine); the managed heap is
negligible (~25 KB); Spectre adds ~10 MB; the GC knobs move nothing
(ConserveMemory has nothing to conserve; server GC is slightly worse).

Recommendation: **keep the 100 MB idle budget, scoped to the TUI without
Roslyn loaded** — the measured range (~75–82 MB) leaves ~20 MB headroom for
the real TUI, and no settings change is proposed for `Lunate.Coding`. Roslyn-
loaded memory is a separate, lazy state (S-4: ~0.5 GB peak on a 47-project
workspace) and gets its own budget when T-25 lands. Memory levers are
runtime/packaging choices, not GC flags.
