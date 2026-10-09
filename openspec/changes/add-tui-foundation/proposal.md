# Proposal: TUI foundation — console IO, VT input, live area (T-18)

## Why

Everything built so far is headless: the loop, the tools, sessions, print mode. The interactive experience begins here. T-18 builds the three pieces the guide's TUI architecture rests on — `IConsoleIO` (all terminal access behind one seam), the VT input layer (raw keys, modifiers, UTF-8, bracketed paste — decided in ADR-0004: our own decoder, never `Console.ReadKey`), and the live area (a merged observable pipeline on System.Reactive with an injected scheduler, per ADR-0007 and the S-5 readability findings). Cards T-19 to T-22 (renderables, tool blocks, key bindings, wiring) all sit on top of this change.

The spikes did their job: S-2 produced the decoder as a reference (32/32 self-test), S-5 compared four concurrency models and its findings shaped ADR-0007's conditions — scheduler injected, Rx confined to the live area, the "drain before read" rule written down as a contract, no `Thread.Sleep`-based tests.

## What changes

- **`Lunate.Tui` becomes real** (new `tui` capability): `IConsoleIO` with a System.Console implementation (raw-mode entry via platform P/Invokes, VT enablement on Windows, fail-soft diagnostics when no console is attached — ADR-0004) and a `FakeConsoleIO` that replays scripted key presses and records frames.
- **VT input decoder**: a productized port of the spike decoder — control bytes, Alt+char, CSI/SS3 with modifiers, tilde keys, UTF-8, bracketed paste, partial-sequence buffering; a lone `Esc` resolves through a quiet-window on the injected scheduler, never a wall clock.
- **Input line**: editing (insert, delete, backspace, cursor, home/end, newline via `Ctrl+J`/`Alt+Enter`), bracketed paste insertion, cursor/paint math through a compact cell-width function (CJK/emoji aware). History and bindings arrive with T-21/T-22.
- **Live area**: immutable state snapshot, one reducer, `Scan` + `Sample` (~30 fps) on the injected `IScheduler`, one writer painting from the snapshot via cursor-up/clear-line sequences; the drain-before-read threading contract is documented and tested with `TestScheduler` (spinner, frame cap, Esc window — no wall-clock waits).
- **Frame snapshots**: fake-backed frames recorded and compared against committed goldens (the repo's existing golden pattern — no new snapshot framework).

## Done when

Frame snapshots pass on the fake console; `TestScheduler` covers every time-based behaviour (30 fps cap, spinner, Esc quiet-window, resize polling); the fail-soft path has tests; `scripts/verify.sh` green including de-AT. The Windows Terminal manual check is a maintainer hand-off (procedure in the card; T-33 owns the full terminal matrix).
