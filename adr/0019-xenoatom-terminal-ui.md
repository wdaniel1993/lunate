# 0019 — XenoAtom.Terminal.UI inline mode: not adopted for the TUI live area and input line

- Status: Proposed — 2026-10-09
- Date: 2026-10-09
- Spike: `docs/spikes/S-6/report.md` (raw evidence under `docs/spikes/S-6/evidence/`)
- Relates to: ADR 0004 (mintty input support), ADR 0005 (startup budgets),
  ADR 0007 (live-area concurrency), ADR 0008 (release publish flags);
  OpenSpec capability `tui` (T-18, T-19)

## Context

Lunate's `Lunate.Tui` builds its own terminal foundation: the `IConsoleIO`
seam, a product VT input decoder (ADR-0004), `InputLine`, `KeyReader`, and a
System.Reactive live area on an injected `IScheduler` capped at ~30 fps
(ADR-0007); T-19 will add an own Markdown subset renderer, scanners,
`TechnicalText` and golden snapshots. [XenoAtom.Terminal.UI](https://github.com/XenoAtom/XenoAtom.Terminal.UI)
3.10.0 offers the same inline-first shape out of the box: `Terminal.Live`
retained-mode live regions with a cell-buffer diff renderer, `Terminal.Write`
flow output above the region, `PromptEditor` for input, and a Markdig-based
`MarkdownControl` in a companion package.

S-6 rebuilt the S-5 scenario (streaming tail, spinner, footer, approval
yes/no/always, steering, Esc cancel, Ctrl+C clear/double quit, resize,
10,000-delta burst) on XenoAtom.Terminal.UI, tested it headlessly through
`InMemoryTerminalBackend` and a reflected internal `TerminalApp.Tick` driver
(50 runs, 1,000 executions, 0 failures), and measured startup, memory,
dependency closure and LoC against the S-5 baseline method. Findings that
drive this decision:

1. **Startup misses the bar.** Whole-process single-file R2R startup is
   53.3 ms median versus 32.1 ms for the baseline — **+21 ms**, at/over the
   ~20 ms adoption bar, with a 47–92 ms range; the first frame is +20 ms
   (1.96 → 22.08 ms) and is one-time framework init.
2. **Packaging risk reproduces.** With the exact verify flags (self-contained,
   single-file, ReadyToRun, compressed) XenoAtom crashed 3/300 starts with
   `AccessViolationException` in `PortableThreadPool.GateThread`; the baseline
   was 0/200. This is the failure class ADR-0007 already tracks as an open
   follow-up — adopting XenoAtom adopts the exposure.
3. **Deterministic test hooks are internal.** The public
   `InMemoryTerminalBackend` covers input injection and output capture, but the
   virtual clock (`TerminalApp.Tick`) and the golden-screen helpers are
   internal; S-6's deterministic tests reach them by reflection. The tui
   spec's golden-frame requirement cannot be met through the public API, and
   the reflection seam is version-fragile in a library with ~30 releases in
   6 months.
4. **The framework owns keys Lunate must own.** `TextEditorBase` consumes
   Ctrl+C as a copy command before KeyDown routing; the S-6 harness had to
   remove that command to honour "Ctrl+C clears input; twice quits". Text
   routing is bubble-only, so approval keys reach the editor first and the app
   clears the line after the fact. PromptEditor's newline binding itself
   changed (Ctrl+N → Shift+Enter) between 3.4.3 and 3.5.0.
5. **One global terminal instance.** There is no public isolated
   `TerminalInstance`; opening a second session disposes the first, so
   snapshot rendering must be sequenced with the live loop.
6. **Markdown is not separable.** `MarkdownControl` lives in a companion
   package but depends on the whole UI/terminal stack (plus Markdig and
   Wcwidth); adopting it costs the same 5-assembly closure and the same
   global-instance constraints, and it contradicts the guide's deliberate
   "no Markdown library, own subset renderer" decision for T-19.
7. **The retirement is real.** Option (a) would delete most of T-18's
   foundation (~1,400 lines today plus platform code) and the System.Reactive
   dependency; that is the strongest argument for adoption, and the reason
   this ADR defines explicit re-open triggers rather than closing the door.

## Decision

**Recommendation: option (c) — stay with ADR-0007.** Build the live area,
input line and Markdown renderer on the accepted T-18/T-19 stack; do not adopt
XenoAtom.Terminal.UI for them now. Keep the S-6 harness and scripts as the
re-evaluation vehicle.

The recommendation is a timing and evidence call, not a verdict on the
library: at 3.10.0 it misses the startup/risk/testability bars Lunate set for
its primary surface, while the cost of switching — rewriting four `tui` spec
requirements, ADR-0004 and ADR-0007, losing frame goldens and depending on a
fast-churning single-maintainer framework — is paid immediately.

### Option (a) — adopt for the live area and input line

Would **retire**:

- T-18: `IConsoleIO`, `SystemConsoleIO`, `FakeConsoleIO`, `ConsoleSize`,
  `ConsoleSupport`; `LiveArea`, `InputLine`, `KeyReader`, `VtInputDecoder`,
  `KeyEvent`/`KeyKind`, `FrameWriter`, `CellText`, `LiveAreaState`,
  `LiveAreaRenderer`; `src/Lunate.Tui/Platform` termios/console-mode code;
  the `System.Reactive` and `Microsoft.Reactive.Testing` pins.
- The `tui` spec requirements "Terminal access through IConsoleIO", "VT input
  decoding", "Input line editing", "Live area threading contract" and their
  scenarios (including fake-console and golden-frame scenarios); ADR-0004 and
  ADR-0007 would be superseded.
- Optionally T-19: `MarkdownRenderer`, own scanners, `TechnicalText`; Spectre
  usage for finished blocks if `Terminal.Write` is used for all blocks.

Would **keep**:

- `Lunate.Agent`'s `IAsyncEnumerable<AgentEvent>` contract and the event
  shapes; approval/steering semantics at the agent boundary; print mode and
  every non-TUI path.
- The culture-invariant formatting rule as a test requirement on whatever
  renders technical readouts.
- ADR-0004's user-facing mintty guidance only in spirit: fail-soft behaviour
  would become XenoAtom's backend behaviour and must be re-verified in the
  terminal matrix (S-6 could not).

Rejected because: +21 ms startup, 3/300 fatal exact-flag starts, no public
deterministic frames, and key-ownership friction; the spec/golden rewrite is
not justified at 3.10.0.

### Option (b) — adopt only the Markdown pieces

Would **retire**:

- T-19: `MarkdownRenderer`, its block/inline scanners and syntax highlighting
  scanners, Markdown and `TechnicalText` golden fixtures; Spectre rendering for
  markdown blocks.
- To keep C#/JSON/shell highlighting a further package
  (`Extensions.CodeEditor.TextMateSharp`) would be needed, or the guide
  requirement would be dropped.

Would **keep**:

- All of T-18 (`IConsoleIO`, live area, `InputLine`, `KeyReader`, Rx,
  ADR-0007); `TechnicalText` would still have to be written (XenoAtom has no
  equivalent); the product keeps Spectre for tool blocks.
- The guide's "no Markdown library" rule must be changed by the maintainer.

Rejected because: `MarkdownControl` cannot be used without the full
XenoAtom.UI/Terminal/Ansi/Markdig closure and the single-global-instance
constraint — the startup, memory and crash findings all still apply — while
delivering something T-19 is specified to build itself, with less control over
goldens and syntax highlighting.

### Option (c) — stay with ADR-0007 (recommended)

Would **retire**: nothing.

Would **keep**: everything in T-18/T-19 as specified. The S-6 results become
risk input for T-20/T-22: keep the live tail bounded, keep frame goldens on
`TestScheduler`, and keep the culture-invariant readouts.

## Consequences

- `Lunate.Tui` keeps `System.Reactive` (ADR-0007), its own decoder
  (ADR-0004) and the T-19 own-renderer plan. No new packages.
- The S-6 evidence is the baseline for a future re-evaluation: the harness,
  the 20-test headless suite and the measurement scripts stay under
  `docs/spikes/S-6/`.
- The packaging crash (ADR-0007 follow-up 1) remains the top release risk for
  timers/thread pools under the exact flags; S-6 adds XenoAtom to the list of
  stacks that exhibit it on this machine.
- **Re-open triggers (any one):**
  1. XenoAtom.Terminal.UI publishes public deterministic test hooks
     (virtual clock, frame-golden capture) and a stability/breaking-change
     policy.
  2. The compressed single-file R2R crash is root-caused and fixed, and
     XenoAtom still shows 0 crashes in a 300-start probe.
  3. The real-terminal matrix (Windows Terminal, conhost, mintty including
     `MSYS=disable_pcon`, macOS Terminal.app, tmux) passes with the S-6
     `--interactive` harness and `TreatControlCAsInput` behaviour is verified.
  4. Whole-process startup delta falls below the ~10 ms S-5 B cost with a
     stable distribution (no 90 ms tail), and idle RSS delta shrinks toward
     the Rx baseline's.
  5. T-18/T-19 maintenance cost proves materially higher than the ~400-line
     glue XenoAtom would replace.
