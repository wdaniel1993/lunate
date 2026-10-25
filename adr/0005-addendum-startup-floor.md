# 0005 (addendum) — The startup gate reads the floor, not the median

- Status: accepted — 2026-10-25 (addendum to ADR 0005; maintainer direction "go for the macOS startup budget")
- Date: 2026-10-25
- Relates to: ADR 0005 (startup budgets), ADR 0008 (release build shape), `docs/spikes/S-3/report.md`
- Amends: ADR 0005

## Context

ADR 0005 calibrated each runner's budget from the median of the measured medians plus ~50 % headroom, and `scripts/perf.sh` gates on the median of 20 hyperfine runs, with one retry before failing.

The macOS leg has since shown that the median is measuring the runner, not the binary. On one branch whose code did not touch the startup path, macOS medians read **102, 312 → 150 (retry), 288, 186, 115, 81, 165 ms** — a 4.5× spread on identical code. The two failing measurements were 329 and 364 ms on a runner where the publish step alone took **ten minutes** (12:41:28 → 12:51:52); a re-run of the same commit measured 312 → 150 ms and passed. The measurement already sits before the test steps (PR #135), so position is not the confound: the machine itself is.

Interference only ever adds time. That is the asymmetry the median ignores: a shared, thermally throttled, over-subscribed runner inflates the middle of the distribution — sometimes for the whole job — while the fastest of the runs still samples what the binary costs when it is not being starved.

## Decision

1. **The gate is the floor.** `scripts/perf.sh` (and its PowerShell twin in `scripts/verify.ps1`) gates on the **fastest of the runs**, and prints the median beside it for context:
   `startup: floor 121 ms (median 329 ms, budget 300 ms)`.
   A miss is reported as `startup budget exceeded: fastest of 20 runs 364 ms > 300 ms (median 380 ms)`.
2. **Budgets are unchanged** — 250 ms Ubuntu, 300 ms macOS, 350 ms Windows, 150 ms local. Their calibration (ADR 0005's rule, `docs/spikes/S-3`) is not re-opened by a metric change; the floor is at or below the median, so no calibrated value becomes tighter in practice.
3. **The noise policy is unchanged** — one retry on a miss, fail on two consecutive misses. With the floor as the statistic, a miss now means "even the fastest run was over budget", which is a much stronger signal than a median miss was.
4. **Both scripts move together, and neither passes on a measurement it could not read.** `scripts/verify.sh` drives `perf.sh`; `scripts/verify.ps1` carries the same measurement inline. Their output lines and failure text stay in step. The JSON parser accepts both shapes in the wild (hyperfine 2.x under `.results[].summary.time_wall_clock`, 1.x at the top level) and **refuses** one that fits neither: the first version of this change read only the 1.x shape, produced an empty string on the macOS runner's hyperfine 2.0.0, and the empty comparison passed the gate vacuously. `scripts/gate-tests.sh` now pins all three: a budget of 1 ms fails, 999999 ms passes, a 2.x measurement is read and gated on its floor, and a JSON without timing fails the step.
5. **Nothing else changes**: same hyperfine flags (warmup 3, 20 runs), same `BUDGET_MS` override, same position in the gate (directly after publish, before the tests).

## Alternatives considered

- **Raise the macOS budget.** Rejected as the primary fix: the good legs measured 81–186 ms, so a budget high enough to absorb a starved runner (≈400 ms) would stop detecting a doubling of the real cost. The observed medians do not support a re-calibration — the runner is bimodal, not uniformly slower.
- **A baseline binary measured in the same job.** The most rigorous answer, and still the fallback if the floor proves insufficient: publish `origin/main` alongside the branch and compare the two floors. Rejected for now because it doubles the publish cost on every leg (ten minutes on the runner that motivated this) to solve a problem the floor already addresses.
- **A cooldown before the retry.** Cosmetic: on the failing runner the second measurement was *worse* than the first (329 → 364), so waiting a few seconds would have changed nothing while adding time to every red gate.
- **Best-of-N with a larger N.** Twenty runs already sample the floor; the failures were not sampling noise but a uniformly slow machine.

## Consequences

- A contended runner no longer reddens the gate; a regression on the startup path still does, because it moves every run including the fastest.
- The gate is slightly more permissive than a median gate at the same number: a change that slows only *some* runs — a bimodal startup, such as a lazily loaded assembly that is sometimes hit before the first frame — can hide under the floor. The median is printed on every run so the trend stays visible in CI history.
- Triage text changes: the step now prints `startup: floor … (median …)` and fails with `startup budget exceeded: fastest of N runs …`. `docs/spikes/S-3/report.md` remains the calibration record and is not touched by this addendum.
