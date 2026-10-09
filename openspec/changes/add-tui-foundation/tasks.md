# Tasks: TUI foundation (T-18)

## 1. IConsoleIO and platform plumbing

- [x] 1.1 `Lunate.Tui` gets System.Reactive 7.0.0 + Microsoft.Reactive.Testing 7.0.0 (Tui + Tui.Tests only); `InternalsVisibleTo Lunate.Tui.Tests`
- [x] 1.2 `IConsoleIO`, `ConsoleSize`, `SystemConsoleIO` (raw-mode entry: Windows `SetConsoleMode`, Unix termios P/Invokes; VT support detection; resize polling on the injected scheduler), `FakeConsoleIO` (scripted keys, recorded writes, scripted size/resize) 
- [x] 1.3 `ConsoleSupport.Check` fail-soft diagnostics (no console / not a terminal / `TERM=dumb`) with tests for every branch
- [x] 1.4 PublicAPI.Unshipped for Tui; tests never touch a real terminal

## 2. VT input

- [x] 2.1 `VtInputDecoder`: port the S-2 decoder — portable self-test cases (32-case table) as xUnit tests, incl. UTF-8, modifiers, tilde keys, SS3, partial sequences, resync
- [x] 2.2 Bracketed paste (markers stripped, newline normalization) and `KeyReader` (bytes → events via `ReadKeysAsync`) with the Esc quiet-window on the injected scheduler; TestScheduler tests for the window and stream reassembly
- [x] 2.3 Round-trip test: scripted byte streams through `KeyReader` produce the expected `KeyEvent` sequences (fed from `FakeConsoleIO`)

## 3. Input line

- [x] 3.1 `InputLine` + `InputLineState`: insert/paste, backspace, delete, left/right/home/end, newline (Ctrl+J / Alt+Enter); pure-state tests for every operation and edge (empty buffer, ends)
- [x] 3.2 `CellText.Width` (combining 0, wide/emoji 2, default 1) with a test table; input-line cursor math uses it; multi-line buffer rendering (display lines, bounded)

## 4. Live area

- [x] 4.1 `LiveAreaState` + reducer + pipeline (`Merge` → `Scan` → `Sample(33 ms)`) with injected `IScheduler`; single-writer render; `FrameWriter` (cursor-up/clear-line painting, area reserve/clear)
- [x] 4.2 TestScheduler tests: 30 fps cap (burst → one frame per window), spinner frames, resize via `Resized`, input editing reflected in frames, sequential render callbacks (single writer)
- [x] 4.3 Frame goldens: scripted session (typing, paste, resize, spinner ticks) → recorded frames vs committed goldens; `LUNATE_TUI_UPDATE_GOLDENS=1` regeneration documented in the test
- [x] 4.4 Contract test: the drain-before-read rule — render reads only sampled snapshots (no cross-thread state reads); no `Thread.Sleep` in any TUI test

## 5. Close

- [x] 5.1 `dotnet csharpier format .`; `bash scripts/verify.sh` green (incl. de-AT); `openspec validate add-tui-foundation --type change --strict`; self-review; commit per group; no push
- [ ] 5.2 Maintainer hand-off documented: Windows Terminal + mintty manual check (procedure modeled on S-2; results recorded in the PR) — maintainer hand-off, not attempted in this apply
