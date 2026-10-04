# S-5 report — Rx vs plain async for the TUI live area

- Date: 2026-10-04
- Change: `run-spike-s5-rx-live-area`
- Question: should the `Lunate.Tui` live area (streaming tail, spinner, status
  footer, approval prompt, steering, cancel/quit) be built on plain async
  (`Channel<T>` + a frame timer) or on Reactive Extensions — and does adding
  ReactiveUI view models for the footer/approval pay for itself?
- Method: one identical scenario implemented four times (baseline, A, B, B+)
  against a shared fake terminal and a replayable `AgentEvent` script; virtual
  time in tests and in the scenario runner; startup/memory from published
  single-file ReadyToRun binaries; one shared xunit test list run 50× per
  variant; a 10,000-delta burst; an adversarial-reviewer readability pass.
- Outcome: **recommend B (System.Reactive), reject B+ (ReactiveUI)** — see
  [ADR 0007](../../../adr/0007-live-area-concurrency.md), **accepted
  2026-10-04** (conditions of adoption in the ADR). B's whole-process startup delta is **+10 ms**
  (baseline 24 ms → 34 ms) and its time-based tests are materially simpler to
  drive than A's; B+ costs 2.6× A's code, duplicates state and hit a
  packaging crash that A/B/baseline did not.
- Raw evidence: [`evidence/`](evidence/).

## What was built

| Piece | What it is |
| --- | --- |
| `S5.Harness/` | Fake terminal (scrollback + recorded frames), replayable `AgentEvent` fixture, canonical `ScenarioScript`, shared `LiveAreaState` reducer, plain-text `LiveAreaRenderer`, Spectre-rendered finished blocks, `SpikeProgram` modes (`--scenario`, `--startup`, `--idle`) |
| `S5.Baseline/` | Same state machine, no concurrency at all: synchronous apply + paint; the delta reference |
| `S5.VariantA/` | Plain async: event/key/resize/frame producers write one `Channel<LiveInput>`; one consumer loop; periodic `TimeProvider` frame timer |
| `S5.VariantB/` | System.Reactive: `Subject<LiveInput>` → `Scan` → `Replay(1).RefCount`; pulse = state changes merged with a busy `Interval`, capped by `Sample(FrameInterval)`; `TestScheduler` virtual time |
| `S5.VariantBPlus/` | B plus ReactiveUI 26 `ReactiveObject` view models for the status footer and approval prompt (`ReactiveCommand` yes/no/always); a render function subscribes to `Changed`; no view bindings |
| `S5.Tests/` | One abstract test list inherited by `VariantATests`/`VariantBTests`/`VariantBPlusTests` (16 tests each) |

The scenario is identical for every variant: replayed `AgentEvent`s (including a
200-delta burst), scripted keys through the fake console, one resize (80×24 →
100×30), steering typed mid-run, an approval answered "always" then auto-approved
a second time, a spinner while a tool runs + footer with model/tokens/context %,
`Esc` cancel, then `Ctrl+C` clear and double-`Ctrl+C` quit inside 2 s.

Because all four share the state machine and renderer, the scenario transcripts
must be byte-identical; they are
([`evidence/scenario-sha1.txt`](evidence/scenario-sha1.txt), sha1
`2416c072…` for all four).

### Package pins (spike only)

| Package | Pin | Licence |
| --- | --- | --- |
| Spectre.Console | 0.57.2 | MIT |
| System.Reactive / Microsoft.Reactive.Testing | 7.0.0 / 7.0.0 | MIT |
| ReactiveUI (→ Core 26.0.1, Binding 9.1.0, Primitives 9.0.0, Disposables 9.0.0, SourceGenerators 4.2.0) | 26.0.1 | MIT |
| Splat (→ Core/Builder/Logging) | 21.0.0 | MIT |
| Microsoft.Extensions.TimeProvider.Testing | 10.4.0 | MIT |
| xunit.v3 | 4.0.1 | Apache-2.0 |

## Check matrix

| # | Check | Baseline | A (plain async) | B (System.Reactive) | B+ (ReactiveUI) | Verdict |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | Startup, single-file R2R no-compression, hyperfine median (20 runs) | 24 ms | 28 ms (+4) | 34 ms (+10) | 37 ms (+13) | B under the ~20 ms bar |
| 1b | Startup, exact verify flags (single-file R2R **compressed**) | 103 ms | 106 ms | 116 ms | unstable (11/100 fatal) | §Startup crash |
| 1c | Internal first-frame median (construct + first paint) | 2.6 ms | 3.5 ms (+0.9) | 10.2 ms (+7.6) | 12.0 ms (+9.4) | Rx init ~7.6 ms; ReactiveUI +1.7 ms on top |
| 2 | Idle peak RSS (sampled, B+ from stable build) | 82.1 MB | 83.5 MB (+1.4) | 89.2 MB (+7.1) | 90.3 MB (+8.2) | negligible |
| 3 | Added assemblies vs baseline | — | +1 | +2 | +13 | B+ is a 13-assembly dependency surface |
| 4 | LoC, variant plumbing only, no tests/harness | 72 | 164 | 114 | 298 | B smallest shell; B+ 2.6× B |
| 5 | Shared test list ×50, all three variants | — | 800 exec | 800 exec | 800 exec | 2400 exec, 0 failures, 0 flakes |
| 6 | 10,000-delta burst per variant | — | pass | pass | pass | no loss/reorder; clean cancel (3/3) |
| 7 | Readability (adversarial-reviewer, 5-sentence flow + traps) | — | easiest | middle | hardest | B+ re-entrant, duplicated state |
| 8 | Dependency health | — | TimeProvider.Testing, MIT, monthly | Rx 7.0.0, MIT, ~yearly | ReactiveUI, MIT, ~4-day churn, no Rx dep | B+ riskiest |
| 9 | Scenario transcript identity | `2416c072…` | `2416c072…` | `2416c072…` | `2416c072…` | PASS all four |

Evidence: [`startup-medians-stable.txt`](evidence/startup-medians-stable.txt),
[`startup-selfreport-medians.txt`](evidence/startup-selfreport-medians.txt),
[`startup-crashes.txt`](evidence/startup-crashes.txt),
[`idle.txt`](evidence/idle.txt), [`assemblies-added.txt`](evidence/assemblies-added.txt),
[`loc.txt`](evidence/loc.txt), [`flake-report.txt`](evidence/flake-report.txt),
[`burst.txt`](evidence/burst.txt), [`readability-review.md`](evidence/readability-review.md),
[`dependency-health.txt`](evidence/dependency-health.txt),
[`scenario-sha1.txt`](evidence/scenario-sha1.txt).

## Detail per check

### 1. Startup and memory (S-3 method)

All four exes were published self-contained, single-file, ReadyToRun for
`osx-arm64` and measured with `hyperfine` (3 warmups, 20 runs) on the same ARM64
macOS dev machine; the table above uses the **no-compression** stable set so the
packaging is identical for every variant. Compressed exact-flags medians are in
[`startup-medians.txt`](evidence/startup-medians.txt).

The internal first-frame number (self-reported: construct the session, feed one
`RunStarted` + text delta, advance one frame, paint) isolates dependency
initialisation from process start: System.Reactive costs ~7.6 ms even when the
session is driven by `TestScheduler`; ReactiveUI/Splat adds ~1.7 ms more. Both
are within the design's ~20 ms adoption bar, but B+ pays it for view models the
review did not find simpler.

Idle memory was sampled from a second process (150 ms peak RSS over a 4 s idle
window). The feed-forward delta is dominated by the added assemblies (~1.4 MB
for A's test provider, ~7 MB for Rx, ~8 MB for Rx + ReactiveUI) and is small
next to the product's baseline.

#### Startup crash under the exact verify flags

The exact CI/verify publish flags (`PublishSingleFile` + `PublishReadyToRun` +
`EnableCompressionInSingleFile`, self-contained) crash **VariantBPlus in 11/100
starts** with `AccessViolationException`, including inside
`System.Reactive.Concurrency.CurrentThreadScheduler`'s static constructor
(`SystemClock.Register` → `new Timer`), and VariantA in **1/100**; Baseline and
VariantB were 0/100 ([`startup-crashes.txt`](evidence/startup-crashes.txt)).
Isolation: B+ is stable framework-dependent (0/200), single-file without R2R
(0/150) and single-file with R2R but without compression (0/150); A is stable
framework-dependent (0/100) but showed 1/100 in the recorded compressed probe
after 0/100 in an earlier manual run — the instability is rare and
timing-dependent.
A tiny System.Reactive-only app that touches `CurrentThreadScheduler` did not
reproduce it (0/200). This looks like a runtime/packaging interaction on this
machine (macOS 26, arm64, dotnet 10.0.103), not variant logic — but it is a
release risk for anything that adds a timer-heavy dependency, and B+'s stable
numbers above are from the same publish minus compression. The spike did not
isolate the root cause; see Limits.

#### Crash probe follow-up (2026-10-04, ADR-0008)

- **CI runners, exact flags:** `win-x64` and `linux-x64` each ran 100 starts ×
  4 variants × both flag sets: **0 crashes** (workflow run 37209985856;
  [`evidence/crash-probe-ci.txt`](evidence/crash-probe-ci.txt)). The crash is
  macOS-specific so far.
- **macOS re-run (independent):** `crash-probe.sh osx-arm64` re-confirmed it —
  VariantBPlus **13/100** compressed, VariantA 0/100 (was 1/100), Baseline and
  VariantB 0/100, all uncompressed 0/100
  ([`evidence/crash-probe-local.txt`](evidence/crash-probe-local.txt)).
- **Minimal repro (macOS, exact flags):** timer-only and Rx+timer apps without
  Lunate code did **not** reproduce it (0/200 compressed and 0/100 uncompressed
  each). No `dotnet/runtime` issue is drafted (the
  reproduce-without-our-code condition is unmet); deeper isolation stays open.

### 2. Lines of code

[`loc.txt`](evidence/loc.txt): Baseline 72, A 164, B 114, B+ 298 (excluding
tests), plus a shared harness of 818 lines. The harness contains the shared
state machine and scenario infrastructure every variant uses, so the variant
column measures only the concurrency plumbing: B does the same job as A in 50
fewer lines; B+ adds 184 lines of view models and view-model synchronisation.
The test list is 367 lines and is literally shared.

### 3. Shared test list, 50× flake

16 tests run against each variant through one `ISession` seam:
tail streaming, 30 fps cap under a burst and sustained deltas, spinner
animation only while a tool runs, footer contents, approval yes/no/always
(always cached), steering queued and shown, `Esc` cancel, `Ctrl+C` clear,
double-`Ctrl+C` inside/outside the 2 s window, resize reflow, and the
10,000-delta burst with clean cancel. 50 full runs → **2400 test executions, 0
failures, 0 flakes** ([`flake-report.txt`](evidence/flake-report.txt); raw runs
in `evidence/flake/`).

Time-based tests are where the variants differ: A's virtual clock needs a drain
barrier that waits for the frame-loop rendezvous (see Surprises), while B/B+
just `TestScheduler.AdvanceBy`. The shared list is identical; the fixture that
makes time work is not.

### 4. 10,000-delta burst

Each variant accumulates the full tail text (not just the rendered window);
after 10k deltas the accumulated text equals the expected concatenation exactly
(no loss, no reorder), then `Esc` leaves `Running`/`ToolRunning`/approval false
and a second `Dispose` is a no-op. Evidence:
[`burst.txt`](evidence/burst.txt) (3/3 passed), and the burst test passes in all
50 flake runs.

### 5. Readability

The `adversarial-reviewer` subagent produced exactly the requested five-sentence
flow per variant plus a hard-to-follow list
([`readability-review.md`](evidence/readability-review.md), reconciliation in
[`readability-review.council.md`](evidence/readability-review.council.md)).
Verdict: **A easiest** (one queue, one consumer, one render gate), **B middle**
(the fold streams one mutable instance while the pulse applies frames outside
the subject), **B+ hardest** (three render triggers, duplicated footer/approval
state, a key handler re-entering the pipeline through a `ReactiveCommand`, and
the 30 fps cap bypassed by view-model `Changed` renders). The one accepted fix
clarified A's apply/render condition; the rest are recorded for T-18/T-19.

### 6. Dependency health

[`dependency-health.txt`](evidence/dependency-health.txt): all pins are MIT
except xunit.v3 (Apache-2.0). System.Reactive 7.0.0 is stable and slow-moving
(last majors 2019/2020/2023/2026); the test scheduler is a separate package
(`Microsoft.Reactive.Testing` 7.0.0). Spectre.Console and Splat release roughly
monthly. **ReactiveUI 26.0.1 is a rewrite**: it no longer depends on
System.Reactive, exposes its own `ReactiveUI.Primitives` (`RxVoid`,
`ISequencer`, signals) and dropped `RxApp`; the metapackage pulled 13 extra
assemblies (Core/Binding/Primitives/Disposables/SourceGenerators/Splat×4). Its
release cadence is ~4 days, and mixing it with System.Reactive forced explicit
disambiguation (`System.ObservableExtensions.Subscribe(...)`) because two
`Subscribe` extensions collide. B+ is two reactive stacks plus Splat for two
small view models.

## Surprises

1. **`PeriodicTimer` + `FakeTimeProvider` is a data race.** The task asked for a
   `PeriodicTimer` frame clock in A. A concurrent
   `PeriodicTimer.WaitForNextTickAsync` races `FakeTimeProvider.Advance`
   (not thread safe) and crashed with `AccessViolationException` in 8/100
   single-file R2R runs. The spike switched A to
   `TimeProvider.CreateTimer` (a periodic callback timer), whose callback runs
   synchronously on the advancing thread, so frames land in the channel before
   the drain barrier and tests are deterministic. This is a deviation from the
   task's wording and the reason A cannot honestly claim "the same ticker in
   test and production". See [`VariantASession.cs`](S5.VariantA/VariantASession.cs).
2. **The exact verify publish combination is flaky in the presence of timers.**
   Single-file + ReadyToRun + compression crashed B+ in ~11% and A in ~1% of
   starts (stack above), while the same binaries without compression, without
   R2R, or framework-dependent were stable. Not isolated to a component.
3. **ReactiveUI 26 is not "Rx + view models" any more.** It carries its own
   reactive primitives and scheduler model, so B+ is B (System.Reactive) plus a
   second reactive stack; that shows up as assembly count, code duplication and
   interop ceremony.
4. **B+ startup is cheap; B+ code is not.** The view models cost ~1.7 ms at
   startup but 184 lines, duplicate footer/approval state, and render outside
   the sampled cap. The design guidance said choose B+ only if the view models
   remove real complexity; they moved it.
5. **The 30 fps cap needed no throttling operator in A.** Gating the paint on
   the frame input gives the same cap as `Sample` in B, so the cap is a
   property of the render gate, not of Rx.
6. **The footer was locale-dependent.** `{ContextPercent:0.#}` formatted with
   the current culture, so on the dev machine (culture `en-AT` — English UI
   with Austrian region, comma decimals) the footer showed `12,5% ctx`,
   one test per variant failed and the scenario transcript sha changed. Fixed
   to invariant formatting in the shared renderer and in the B+ view model —
   which duplicated the format string (the readability review's "duplicated
   state" point, observed in practice). Technical readouts must be
   culture-invariant; T-18/T-19 tests must not depend on the machine locale.
   The committed scenario evidence (sha `2416c072…`) is invariant and
   reproduces after the fix.

## Limits

- One machine, one day, `osx-arm64` only: startup/memory numbers are a
  snapshot, not a benchmark; the deltas are the signal.
- The whole-process medians use compression-off builds for comparability
  because the exact-flags B+ build is unreliable; A/B/baseline exact-flags
  numbers are recorded too.
- The packaging crash root cause was not isolated (A 1%, B+ 11%); it needs a
  follow-up on all three RIDs and ideally a minimal repro before ReactiveUI is
  ever adopted.
- Variant A's draw loop and A's/B's thread-affinity contracts are spike-grade:
  tests follow the implicit "drain before read" rule, which the reviewer
  flagged and which T-18 must make explicit.
- The spike drives B/B+ with `TestScheduler` in both tests and the scenario exe;
  production would take an `IScheduler` (default scheduler / event-loop). The
  `TestScheduler` seam is what makes the time tests simple and is the reason
  the decision leans B.
- No real terminal/ANSI, no real agent loop, no approval round-trip to an
  agent: the `AgentEvent` mirror and the fake console are the whole world. The
  shared state machine means LoC measures plumbing, not semantics.
- Readability is one reviewer from one model family; it is corroborated by LoC
  and the test-shape observations but not replicated.
- Spike code is throwaway, lives outside `lunate.sln`, and changes nothing under
  `src/`.

## Recommendation

**Adopt B (System.Reactive) for the TUI live area; keep plain async elsewhere;
do not adopt ReactiveUI for it now.** B keeps the `IAsyncEnumerable<AgentEvent>`
contract and converts at the TUI boundary, its startup delta (+10 ms
whole-process, +7.6 ms internal) is under the ~20 ms bar, its time-based tests
are the simplest (`TestScheduler` in both directions), and its plumbing is the
smallest. B+ is rejected on code, duplication, dependency churn and packaging
risk. Full rationale and the conditions for adoption (pins, scheduler seam,
T-18/T-19 test requirements, the open packaging crash) are in
[`adr/0007-live-area-concurrency.md`](../../../adr/0007-live-area-concurrency.md),
status **accepted (2026-10-04)**.
