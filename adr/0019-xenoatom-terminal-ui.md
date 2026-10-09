# 0019 — XenoAtom.Terminal.UI inline mode: not adopted for the TUI live area and input line

- Status: accepted — 2026-10-09 (maintainer sign-off; recommendation (c) adopted: stay with ADR-0007)
- Date: 2026-10-09
- Spike: `docs/spikes/S-6/report.md` (raw evidence under `docs/spikes/S-6/evidence/`)
- Relates to: ADR 0004 (mintty input support), ADR 0005 (startup budgets),
  ADR 0007 (live-area concurrency), ADR 0008 (release publish flags);
  OpenSpec capability `tui` (T-18, T-19)

## Context

Lunate's `Lunate.Tui` builds its own terminal foundation: the `IConsoleIO`
seam, a product VT input decoder (ADR-0004), `InputLine`, `KeyReader`, and a
System.Reactive live area on an injected `IScheduler` capped at ~30 fps
(ADR-0007); T-19 adds the Markdown subset renderer, scanners, `TechnicalText`
and golden snapshots. [XenoAtom.Terminal.UI](https://github.com/XenoAtom/XenoAtom.Terminal.UI)
3.10.0 offers the same inline-first shape out of the box: `Terminal.Live`
retained-mode live regions with a cell-buffer diff renderer, `Terminal.Write`
flow output above the region, `PromptEditor` for input, and a Markdig-based
`MarkdownControl` in a companion package.

S-6 rebuilt the S-5 scenario (streaming tail, spinner, footer, approval
yes/no/always, steering, Esc cancel, Ctrl+C clear/double quit, resize,
10,000-delta burst) on XenoAtom.Terminal.UI, tested it headlessly through
`InMemoryTerminalBackend` and a reflected internal `TerminalApp.Tick` driver
(50 runs, 1,000 executions, 0 failures), and measured startup, memory,
dependency closure and LoC against the S-5 baseline method. The decision rests
on three decisive reasons; the measurements are supporting data.

**Decisive:**

1. **Testability: deterministic frames and goldens need internals.** The
   public `InMemoryTerminalBackend` covers input injection and output capture,
   but the virtual clock (`TerminalApp.Tick`) and the golden-screen helpers are
   internal; S-6's deterministic tests reach them by reflection. The `tui`
   spec's golden-frame requirement cannot be met through the public API, and
   the reflection seam is version-fragile in a fast-churning library. An
   upstream request for a public deterministic driver is drafted at
   `docs/spikes/S-6/upstream-issue.md`.
2. **Input model friction.** `TextEditorBase` consumes Ctrl+C as a copy
   command before KeyDown routing; the S-6 harness had to remove that command
   to honour "Ctrl+C clears input; twice quits". Text routing is bubble-only,
   so approval keys reach the editor first and the app clears the line after
   the fact; PromptEditor's newline binding changed (Ctrl+N → Shift+Enter)
   between 3.4.3 and 3.5.0. There is no public isolated `TerminalInstance` —
   opening a second session disposes the first, so snapshot rendering must be
   sequenced with the live loop.
3. **Dependency churn.** ~30 releases in 6 months, one maintainer, no
   breaking-change policy; key bindings changed between patch releases. The
   stack it would replace (Rx 7.0.0 slow-moving; Spectre monthly) churns far
   slower.

**Supporting data (within budget):**

- Whole-process startup: 53.3 ms median vs 32.1 ms baseline — +21 ms
  (47–92 ms range). **53 ms absolute sits within the ADR-0005 budget**; the
  delta is one-time framework init (first frame +20 ms). The unstable tail,
  not the absolute value, is the risk.
- Idle peak RSS: 101.2 MB vs 76.0–86.9 MB baseline — +14…+25 MB, **within the
  ADR-0009 gate of 150 MB**; roughly double S-5's +7 MB Rx variant.
- Added assemblies: +5 (XenoAtom.Terminal.UI, Terminal, Ansi, Markdig,
  Wcwidth); dependency closure 6 packages.

The retirement is real: option (a) would delete most of T-18's foundation
(~1,400 lines today plus platform code) and the System.Reactive dependency;
that is the strongest argument for adoption, and the reason this ADR defines
explicit re-open triggers rather than closing the door.

## Decision

**Recommendation: option (c) — stay with ADR-0007.** Build the live area,
input line and Markdown renderer on the accepted T-18/T-19 stack; do not adopt
XenoAtom.Terminal.UI for them now. Keep the S-6 harness and scripts as the
re-evaluation vehicle.

The recommendation is a timing and evidence call, not a verdict on the
library: at 3.10.0 it fails Lunate's testability bar for its primary surface,
fights the key-ownership model Lunate must control, and churns roughly five
times faster than the stack it would replace — while the cost of switching
(rewriting four `tui` spec requirements, ADR-0004 and ADR-0007, losing frame
goldens) is paid immediately.

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

Rejected because: no public deterministic frames or virtual clock,
key-ownership friction, and dependency churn — with startup (+21 ms; 53 ms
absolute) and idle (+14…+25 MB) inside the ADR-0005/ADR-0009 budgets as
supporting data. The spec/golden rewrite is not justified at 3.10.0.

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

Rejected because: `MarkdownControl` cannot be used without the full
XenoAtom.UI/Terminal/Ansi/Markdig closure and the single-global-instance
constraint — a whole-stack dependency for a piece T-19 is specified to build
itself, with less control over goldens and syntax highlighting. The parser
question is not this ADR's to decide: whether T-19 uses Markdig (parser only)
with our own Spectre renderer is settled in the T-19 change — Markdig 1.4.0
itself is BSD-2-Clause and stable.

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
- The macOS `AccessViolationException` (compressed single-file R2R — **not a
  shipped configuration since ADR 0008**) reproduced with XenoAtom (3/300
  starts). Recorded as a follow-up in ADR 0008: a third, unrelated dependency
  stack exhibiting it strengthens the runtime hypothesis.
- **Re-open triggers (any one):**
  1. A public deterministic test driver (`TerminalApp.Tick` or equivalent) is
     available, so golden frames and virtual time need no reflection.
  2. A stability/breaking-change policy exists (today: ~30 releases in
     6 months, none).
  3. The compressed single-file R2R crash (not a shipped configuration since
     ADR 0008) is root-caused and fixed, removing the known runtime-level risk
     class.
  4. The real-terminal matrix (Windows Terminal, conhost, mintty including
     `MSYS=disable_pcon`, macOS Terminal.app, tmux) passes with the S-6
     `--interactive` harness and `TreatControlCAsInput` behaviour is verified.
  5. Whole-process startup delta falls below the ~10 ms S-5 B cost with a
     stable distribution (no 90 ms tail), and idle RSS delta shrinks toward
     the Rx baseline's.
  6. T-18/T-19 maintenance cost proves materially higher than the ~400-line
     glue XenoAtom would replace.
