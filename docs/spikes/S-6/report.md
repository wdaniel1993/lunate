# S-6 report — XenoAtom.Terminal.UI inline mode vs the T-18/T-19 stack

- Date: 2026-10-09
- Change: spike-only (branch `spike/s6-xenoatom`; no product change; ADR
  0019 proposed)
- Question: should `Lunate.Tui` replace its own live area and input line with
  **XenoAtom.Terminal.UI 3.10.0** inline mode (`Terminal.Write` for finished
  blocks, `Terminal.Live` for the live area, `PromptEditor` for input and
  steering, the Markdig `MarkdownControl` for markdown) while keeping
  scrollback-first inline output?
- Method: rebuilt the S-5 scenario on XenoAtom.Terminal.UI (same
  `AgentEvent` mirror, same `ScenarioScript`, same approval/steering/Esc/Ctrl+C
  rules), drove it through the real inline host loop against
  `InMemoryTerminalBackend`, ran the same test list where the API allows it
  (50× flake), and measured startup, idle memory, dependency closure, LoC and
  the 10,000-delta burst against the S-5 baseline method.
- Outcome: **recommend (c) stay with ADR-0007** — XenoAtom.Terminal.UI 3.10.0
  misses the decisive bars: **+21 ms** whole-process startup (**+20 ms** first
  frame) versus the ~20 ms adoption bar and S-5 B's accepted +10 ms; a
  **fatal `AccessViolationException` in 3/300** exact-flag starts (baseline
  0/200); no public deterministic frame stepping (the library's own
  `TerminalApp.Tick` is internal and reached only by reflection); and key
  bindings that change within days. Retiring T-18/T-19 for this is not
  justified now. See [ADR 0019](../../../adr/0019-xenoatom-terminal-ui.md).
- Raw evidence: [`evidence/`](evidence/).

## What was built

| Piece | What it is |
| --- | --- |
| `S6.Harness/` | The S-5 scenario on XenoAtom.Terminal.UI: `LiveVisual` (retained tree: tail `TextBlock` capped at 6 rows, steering lines, spinner + footer `HStack`, status, approval prompt, `PromptEditor`), `LiveModel` (S-5 state machine ported), `XenoLiveApp` (`Terminal.Live` host loop, `Terminal.Write(new MarkdownControl(...))` for finished blocks, event queue, spinner pulse), `ScenarioReplay` (wall-clock replay of the canonical script), `AnsiScreen` (spike ANSI screen model), modes `--scenario`, `--startup`, `--idle`, `--burst`, `--interactive` |
| `S6.Tests/` | 20 tests: 6 deterministic-render tests through a reflected `TerminalApp.Tick` driver, 14 live-loop tests through `InMemoryTerminalBackend`, plus the full scenario replay |
| `scripts/` | `run-flake.sh`, `run-scenario.sh`, `measure-startup.sh`, `loc.sh`, `manual-checks.sh` |
| `evidence/` | Research notes, scenario transcripts and raw ANSI, startup JSON/medians, crash probe, idle RSS, assembly diff, dependency closure, LoC, flake report; full list at [Where the evidence lives](#where-the-evidence-lives) |

The scenario is the S-5 script verbatim: streaming tail with a 200-delta
burst, steering typed mid-run (`also check the tests`), approval answered
`always` then auto-approved, a tool run with spinner and usage footer, one
resize (80×24 → 100×30), `Esc` cancel, then `Ctrl+C` clear and double-`Ctrl+C`
quit inside 2 s. Only the substrate changed. The tail, footer, approval and
steering semantics are asserted through the S-5 rules.

### Package pins (spike only)

| Package | Pin | Licence |
| --- | --- | --- |
| XenoAtom.Terminal.UI | 3.10.0 | BSD-2-Clause |
| XenoAtom.Terminal.UI.Extensions.Markdown | 3.10.0 | BSD-2-Clause |
| XenoAtom.Terminal (transitive) | 2.2.0 | BSD-2-Clause |
| XenoAtom.Ansi (transitive) | 1.7.1 | BSD-2-Clause |
| Markdig (transitive) | 1.4.0 | BSD-2-Clause |
| Wcwidth (transitive) | 4.0.1 | MIT |
| xunit.v3 (tests) | 4.0.1 | Apache-2.0 |

## Research findings: XenoAtom.Terminal.UI

Full notes in [`evidence/research-xenoatom.txt`](evidence/research-xenoatom.txt).

- **Maintainer:** Alexandre Mutel (xoofx), XenoAtom organisation (also
  Markdig, dotnet-releaser, lunet). Repository created 2026-01-07; ~1,028
  commits, ~288 stars at the time of writing.
- **Licence:** BSD-2-Clause, verified in both places: the repository
  `license.txt` and the NuGet package metadata (`licenses.nuget.org/BSD-2-Clause`)
  for XenoAtom.Terminal.UI, XenoAtom.Terminal, XenoAtom.Ansi and the Markdown
  extension.
- **Version history (last 6 months, 2026-04-09…2026-10-09):** 30 releases.
  Representative dates: 3.2.0 (2026-05-04), 3.5.0 (05-16), 3.6.0 (05-24),
  3.7.0 (06-01), 3.8.0 (07-19), 3.9.0 (08-07), 3.10.0 (09-22). May alone had
  11 releases; March–April sat on 3.0–3.1.
- **Cadence:** weekly-to-daily patches (3.5.0–3.5.5 in five days,
  3.4.3→3.5.0 on the same day), gaps of 4–7 weeks between recent minors.
- **Breaking/behavioural changes found:** the PromptEditor newline gesture
  changed from Ctrl+N to Shift+Enter between 3.4.3 and 3.5.0 (Ctrl+N kept as
  fallback); the hosting docs record that `Terminal.Live`/`Run` now default to
  `LoopMode = Auto` and that `UpdateWaitDuration` is no longer the frame
  cadence control; 3.9.0 added configurable scroll bar visibility. Releases are
  dotnet-releaser generated change lists with no breaking-change policy or
  migration guide.
- **Requirements:** net10.0 only; the integration relies on C# 14 extension
  members (`Terminal.Write/Live/Run` are extension members in the UI package).
- **Inline story:** `Terminal.Live(visual, onUpdate)` hosts a retained visual
  tree in a cell-buffer-diff inline region; `Terminal.Write(visual)` writes
  finished blocks above it; `PromptEditor` is the promoted prompt input control;
  `MarkdownControl` exists but lives in the separate
  `XenoAtom.Terminal.UI.Extensions.Markdown` package (Markdig ≥ 1.4.0), not in
  the core.
- **Pins used:** XenoAtom.Terminal.UI 3.10.0 and
  XenoAtom.Terminal.UI.Extensions.Markdown 3.10.0.

## Check matrix

Baseline = the accepted T-18/T-19 stack: `IConsoleIO` + own VT decoder +
`InputLine` + System.Reactive live area (ADR-0007) and the T-19 own Markdown
renderer + Spectre blocks. Startup/memory baseline numbers are re-measured in
this spike from `docs/spikes/S-5/S5.Baseline` with the S-5 publish method
(single-file ReadyToRun, self-contained, compression off); the acceptance bar
for a TUI addition is the S-5/ADR-0007 ~20 ms.

| # | Check | Baseline (T-18/T-19 stack) | XenoAtom.Terminal.UI 3.10.0 | Verdict |
| --- | --- | --- | --- | --- |
| 1 | Whole-process startup, single-file R2R no compression, hyperfine 3×20 | 32.1 ms median (min 31.2, max 33.0) | 53.3 ms median (min 47.2, **max 92.1**, σ 15.6) | **+21.2 ms**, at/over the ~20 ms bar, unstable |
| 1b | Self-reported first frame (construct + first paint) | 1.96 ms | 22.08 ms | **+20.1 ms** framework init |
| 1c | Exact verify flags (compressed) 100 starts | 0 fatal | **1/100**, then 2/200 in the detail probe | release risk (see §1c) |
| 2 | Idle peak RSS, 150 ms sampling | 76.0 MB (run A) / 86.9 MB (run B) | 101.2 MB / 101.2 MB | **+14…+25 MB**, stable ≈99 MB |
| 3 | Added assemblies vs baseline | Spectre.Console ×2 (T-19) + Rx ×2 (ADR-0007) | **+5** (XenoAtom.Terminal.UI, Terminal, Ansi, Markdig, Wcwidth); closure 6 packages | 5-assembly surface incl. a Markdown engine |
| 4 | LoC (harness + glue, no tests) | S-5: shared harness 818 + B plumbing 114 | total 1,435; Xeno-specific: model 275 + visual 143 + app 246; S-5-identical scenario infra 188; drivers 258; screen model 325 | comparable; retirement is the real gain |
| 5 | Tests without a real terminal | `FakeConsoleIO` + `TestScheduler`, public; golden frames exact | `InMemoryTerminalBackend` public (input + output); deterministic ticks only via **reflection into internal APIs**; no byte-stable frame goldens | weaker, version-fragile |
| 6 | S-5 shared test list | 16 tests × variants | 20 tests (13 live/behaviour + 6 deterministic + scenario) | covered |
| 7 | Flake, 50 runs (S-5 method) | 2,400 exec, 0 failures | **1,000 exec, 0 failures, 0 flakes** | pass |
| 8 | 10,000-delta burst | pass 3/3, no loss/reorder, clean cancel | **pass 3/3**, 48,890 chars exact, clean cancel, 3–5 ticks | pass |
| 9 | Scenario transcript identity | sha `2416c072…` across all variants | final screen/state stable sha `caaec73d…` across 3 runs; **tick counts vary** (91–321) | weaker: stable end state, not frame goldens |
| 10 | Input interception | own decoder per ADR-0004 | Ctrl+C copy command consumes the gesture before routing; app must remove a built-in command; approval text hits the editor first and is cleared after | friction |
| 11 | Dependency health | Rx 7.0.0 slow-moving; Spectre monthly; MIT | 30 releases/6 months, one maintainer, no breaking-change policy, key bindings changed in days | riskiest of the two |

## Detail per check

### 1. Startup and memory (S-5 method)

Both binaries were published self-contained, single-file, ReadyToRun for
`osx-arm64` with compression off, then measured with `hyperfine` (3 warmups,
20 runs) on the same machine (macOS 26.3.1, arm64, .NET 10.0.103, hyperfine
1.20.0). Medians in
[`startup-medians-stable.txt`](evidence/startup-medians-stable.txt), raw runs
in [`startup-S5Baseline-stable.json`](evidence/startup-S5Baseline-stable.json)
and [`startup-XenoAtom-stable.json`](evidence/startup-XenoAtom-stable.json).

The self-reported first frame (construct the app, start the inline loop, paint
once) isolates dependency initialisation: 1.96 ms → 22.08 ms. The delta is
almost entirely one-time framework initialisation (theme/controls statics,
terminal backend, Markdig reachable from the harness assembly), not process
start. The whole-process median delta (+21.2 ms) matches it, but XenoAtom's
max (92.1 ms) shows the JIT/init path is far less predictable than the
baseline's 0.5 ms σ.

#### 1c. Fatal starts under the exact verify flags

The exact CI/verify combination (`PublishSingleFile` + `ReadyToRun` +
`EnableCompressionInSingleFile`, self-contained) crashed **1/100** starts,
and the follow-up detail probe reproduced **2/200** more. All were
`Fatal error. System.AccessViolationException` inside
`PortableThreadPool.GateThread` (`WaitHandle.WaitOneNoCheck` /
`GC.GetGCMemoryInfo`); stacks in
[`startup-crash-detail.txt`](evidence/startup-crash-detail.txt), counts in
[`startup-crashes.txt`](evidence/startup-crashes.txt). The S-5 baseline was
0/100 and 0/100 again here. This is the same failure class ADR-0007 recorded
(A 1/100, B+ 11/100, root cause never isolated and still an open follow-up);
XenoAtom is not uniquely responsible, but adopting it adopts the exposure.
The stable-set numbers above (compression off) are unaffected.

#### 2. Idle memory

Peak RSS over a 4 s idle window, sampled every 150 ms with the S-5 sampler
([`idle.txt`](evidence/idle.txt), [`idle-XenoAtom.txt`](evidence/idle-XenoAtom.txt),
[`idle-S5Baseline.txt`](evidence/idle-S5Baseline.txt)). XenoAtom was 101.2 MB
in both runs; the baseline moved 76.0–86.9 MB between runs. The feed-forward
delta is therefore +14…+25 MB — roughly double the +7 MB System.Reactive
variant measured in S-5 — and is dominated by the added assemblies plus the
retained visual tree, control themes and Markdig.

### 3. Lines of code and retirement surface

[`loc.txt`](evidence/loc.txt): harness 1,435 lines (11 files), tests 552,
scripts 196. Of the harness, 188 lines are S-5 scenario infrastructure copied
verbatim (`AgentEvent`, `ScenarioScript`, `Keys`, `Scenario`, `Spinner`),
325 are the spike's ANSI screen model, 258 are mode/replay drivers, and 664
are the XenoAtom port (`LiveModel`, `LiveVisual`, `XenoLiveApp`). A fair
comparison is the Xeno-specific ~660 lines against S-5's 818-line shared
harness plus 114-line variant plumbing; the point is not LoC but what
retirement would buy — see the ADR's retire/keep lists. XenoAtom adoption
would delete roughly the whole `src/Lunate.Tui` foundation (1,399 lines across
13 files today, plus tests and the platform termios code) in exchange for the
~400-line visual/loop glue plus the framework dependency.

### 4. Testability without a real terminal (the adoption gate)

What exists, verified in the package and the library source:

- **Public:** `InMemoryTerminalBackend` captures output (`GetOutText()`),
  injects events (`PushEvent`, `SetSize(..., raiseEvent: true)`), and
  `Terminal.Open(backend, options, force: true)` + `TerminalInstance.Write`
  render one-shot visuals. The S-6 live-loop tests (input, approvals,
  steering, Esc, Ctrl+C window, resize, finished-block flow, burst) run
  entirely on this backend.
- **Internal:** the library's own tests step frames deterministically with
  `TerminalApp.BeginRun/Tick(long)/EndRun` and an injected update callback; the
  `TerminalAppTestDriver` and `AnsiTestScreen` helpers are internal. The S-6
  deterministic tests reach the same hooks **by reflection**
  (`S6.Tests/TickDriver.cs`), which works today but is an unsupported seam.
- **Not available:** a public virtual clock, and byte-stable frame transcripts
  from the live loop — the host is deadline/event driven and coalesces
  invalidations, so frame counts and diff output vary run to run. The tui
  spec's "golden frames" and S-5's transcript identity cannot be met through
  the public API.
- **Singleton constraint:** only one global `TerminalInstance` can be open;
  opening another (`force: true`) disposes the first. Snapshot renders and the
  live loop must be sequenced, which the tests work around by stopping the loop
  first.

### 5. Flake and scenario

Fifty full runs: **1,000 test executions, 0 failures, 0 flakes**
([`flake-report.txt`](evidence/flake-report.txt), raw runs in
[`evidence/flake/`](evidence/flake/)). The suite is deterministic in the
reflection-driven tests and tolerant (polling with timeouts, injected clock for
the 2 s window) in the live-loop tests; a run takes ~5.5 s.

The canonical scenario ran three times. Normalised final transcripts are
identical — sha `caaec73d…` ([`scenario-sha1.txt`](evidence/scenario-sha1.txt),
transcripts [`scenario-run-1.txt`](evidence/scenario-run-1.txt) …
[`scenario-run-3.txt`](evidence/scenario-run-3.txt)) — but the loop tick count
varies with wall-clock scheduling (91–321), which is why normalisation strips
it. The raw ANSI stream of run 1 is kept as
[`scenario-run-1.ansi`](evidence/scenario-run-1.ansi). The finished block is
written with `Terminal.Write(new MarkdownControl(...))` and renders as
`bash ok: 48 passed`; the live area keeps the streaming tail, `queued: also
check the tests`, the footer (`gpt-5.1-codex · 1540 tok · 12.5% ctx`) and the
final `input cleared (Ctrl+C again within 2 s quits)` status.

### 6. Burst

`--burst` pushes 10,000 deltas (`0;1;…;9999;`), then Esc. Three runs: exact
accumulation (48,890 chars), no loss/reorder, clean cancel, 3–5 loop ticks
([`burst.txt`](evidence/burst.txt)). The test suite's burst test passes in all
50 flake runs.

### 7. Dependency closure and health

[`assemblies-added.txt`](evidence/assemblies-added.txt) and
[`assembly-counts.txt`](evidence/assembly-counts.txt): +5 assemblies
(XenoAtom.Terminal.UI, XenoAtom.Terminal, XenoAtom.Ansi, Markdig, Wcwidth).
[`dependency-closure.txt`](evidence/dependency-closure.txt): 6 packages in the
closure including the Markdown extension. The baseline's framework-dependent
publish carries Spectre.Console ×2; the product stack adds Rx ×2 under
ADR-0007. Health: BSD-2-Clause, one maintainer, ~30 releases in 6 months with
unannounced key-binding changes, no migration policy. Markdig 1.4.0 and
Wcwidth 4.0.1 are older, stable dependencies but arrive only because the
Markdown control does.

## Where the evidence lives

All under `docs/spikes/S-6/evidence/`:

| File | Content |
| --- | --- |
| `research-xenoatom.txt` | Maintainer, licence (repo + NuGet), version history, cadence, breaking changes, testing surface |
| `startup-medians-stable.txt`, `startup-S5Baseline-stable.json`, `startup-XenoAtom-stable.json`, `startup-selfreport.txt` | Hyperfine 20-run startup sets and self-reported medians |
| `startup-crashes.txt`, `startup-crash-detail.txt` | Exact-flag 100-start probe and the reproduced `AccessViolationException` stacks |
| `idle.txt`, `idle-S5Baseline.txt`, `idle-XenoAtom.txt` | Peak-RSS samples |
| `assemblies-*.txt`, `assembly-counts.txt`, `dependency-closure.txt` | Dependency surface |
| `loc.txt` | Lines of code per file |
| `flake-report.txt`, `flake/` | 50×0 failures, raw runs |
| `scenario-run-{1,2,3}.txt`, `.normalized.txt`, `scenario-run-1.ansi`, `scenario-sha1.txt` | Scenario transcripts (raw and normalised) and the raw ANSI stream |
| `burst.txt` | 3×10,000-delta runs |

## Surprises

1. **The compressed-package crash class is not ReactiveUI's.** XenoAtom.UI hit
   the same `AccessViolationException` in `PortableThreadPool.GateThread`
   (3/300 exact-flag starts); trusting the stable-set numbers requires
   compression off until ADR-0007's follow-up 1 is closed.
2. **Deterministic testing exists, but only internally.** `TerminalApp.Tick`
   gives virtual frames; the library's own tests use it; external consumers
   must reflect. That is a make-or-break gate that currently reads "possible
   but unsupported".
3. **Startup is almost all first-use framework init** (+20 ms first frame vs
   +21 ms whole process), and the tail is unstable (47→92 ms).
4. **Ctrl+C is not the app's key.** `TextEditorBase` binds Ctrl+C to a copy
   command that consumes the gesture before KeyDown routing; Lunate's
   clear/double-quit contract required removing a built-in command through the
   public `Commands` list. Bubble-only text routing means approval keystrokes
   reach the focused editor first and are cleared after the fact.
5. **One global terminal instance.** There is no public isolated instance;
   opening a second session disposes the first, so one-shot snapshot rendering
   must be sequenced with the live loop (the library's own tests only work
   because they are inside the package).
6. **The inline region grows with the visual.** An unbounded tail wraps to as
   many rows as it needs; the harness had to cap the tail `TextBlock` at six
   rows to stay scrollback-first. S-5's fixed six-line tail was a renderer
   property, not a given.
7. **Markdown is a full visual control, not a string renderer.** Using it
   pulls the UI stack (its whole point is `DocumentFlow`/scroll integration)
   and Markdig; the markdown-only option cannot avoid the terminal dependency.
8. **`dotnet test` discovers zero tests for these MTP projects in this SDK**
   (true for the archived S-5 suite as well); the flake script runs the test
   dll directly. Worth a repo follow-up regardless of S-6.

## Limits

- One machine (macOS 26.3.1, arm64, .NET 10.0.103), one day; startup/memory
  are snapshots, not benchmarks. The deltas are the signal.
- **No real terminal was used.** Everything ran on
  `InMemoryTerminalBackend`; raw ANSI was modelled by the spike's own screen
  parser, not a terminal emulator. Windows/Linux startup, memory and the crash
  class were not measured.
- The scenario runs in wall time; frame counts vary and the transcript is
  stable only after stripping ticks — no byte-stable frame goldens.
- Markdown was exercised with one tool-result block; tables, alerts, code
  fences and link rendering were not evaluated.
- Only the interaction set in the S-5 script was tested (typing, Enter, Esc,
  Ctrl+C, resize, approvals); arrow-key editing, paste, IME, mouse, and the
  fullscreen mode were not.
- The reflection tick driver is version-fragile by construction; a 3.x bump
  can break it silently.
- The harness caps the tail at six rows by hand; product behaviour without
  that cap was not studied beyond noting it grows.

### What the maintainer must check on real terminals

Nothing here is verifiable on the headless backend; run `--interactive`
(published by `scripts/manual-checks.sh`) in each environment, record OS,
terminal and `TERM`, and capture the screen:

1. **Windows Terminal** (PowerShell and Git Bash profiles): typing, Enter
   steering, `y`/`n`/`a` approvals, `Esc` cancel, `Ctrl+C` clear and double
   quit within 2 s, resize reflow, finished block placement above the live
   region, colors, cursor visibility, no leftover wrapped rows.
2. **conhost** (`cmd.exe` legacy console): same checklist.
3. **Git Bash (mintty)** launched directly, then with `MSYS=disable_pcon`:
   does XenoAtom's backend fail soft with an actionable message (ADR-0004) or
   throw? Does `TreatControlCAsInput` work, or does Ctrl+C arrive as a signal?
4. **macOS Terminal.app**: same checklist.
5. **tmux**: same checklist, plus resize while streaming and one split-pane
   run.
6. **SSH/nested remote session** (if available): Ctrl+C and bracketed paste
   behaviour, OSC-52 clipboard side effects.
7. **Unicode**: CJK/emoji cursor math in `PromptEditor`, and paste of multi-line
   text without Enter triggering submit.

## Recommendation

**Stay with the accepted stack: option (c).** Do not adopt
XenoAtom.Terminal.UI for the live area and input line now, and do not adopt
its Markdown control as a T-19 shortcut. Full rationale, the retire/keep lists
for all three options and re-open triggers are in
[ADR 0019](../../../adr/0019-xenoatom-terminal-ui.md) (status **proposed**).

Decisive numbers:

- **+21 ms** whole-process startup and **+20 ms** first frame against the
  ~20 ms adoption bar and S-5 B's accepted +10 ms — before any product
  feature is built on it.
- **3/300 exact-flag starts fatal** (`AccessViolationException` in the thread
  pool; baseline 0/200) while ADR-0007's packaging follow-up is still open.
- **Deterministic frames and byte-stable transcripts are not public API**; the
  tui spec's golden-frame requirement would be rewritten and tests would lean
  on reflection into a 30-releases-per-6-months library.
- **Markdown-only is a trap:** `MarkdownControl` needs the full UI/terminal
  stack and Markdig, contradicts the guide's "no Markdown library" decision,
  and still needs `TechnicalText` work for the footer.

Retirement is the genuine attraction — option (a) would retire the entire
`IConsoleIO`/live-area/input/decoder foundation and the System.Reactive
dependency. If the library stabilises (public deterministic test hooks, a
breaking-change policy, a fixed packaging crash, verified terminal matrix),
re-run this spike; the harness and scripts are kept for exactly that.
