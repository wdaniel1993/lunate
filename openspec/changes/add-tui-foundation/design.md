# Design: TUI foundation (T-18)

## Packages (maintainer-gated)

`Lunate.Tui` takes its first real dependencies, exactly as ADR-0007 pins them: **System.Reactive 7.0.0** and **Microsoft.Reactive.Testing 7.0.0** (MIT), confined to the live area; `Lunate.Agent`/`Ai`/`Protocols`/`Coding` do not take them. No snapshot framework: frame goldens follow the repo's existing golden-file pattern.

## `IConsoleIO` — all terminal access behind one seam

Public surface (small on purpose):

- `bool IsInteractive` — input is a console and output can carry ANSI/VT.
- `ConsoleSize Size { get; }` (columns, rows).
- `IObservable<ConsoleSize> Resized { get; }` — distinct-until-changed; production drives it by polling `Size` on a 250 ms interval on the injected scheduler (no OS-specific resize API; SIGWINCH would add a platform branch for no behavior difference — revisit in T-33 if a poll shows up in profiles).
- `IAsyncEnumerable<KeyEvent> ReadKeysAsync(CancellationToken ct)` — decoded VT input; production reads raw stdin bytes and feeds the decoder incrementally.
- `void Write(string text)` — raw write (the live area composes cursor-up/clear-line sequences itself); no styling layer here (Spectre arrives with T-19).
- `IDisposable EnterRawMode()` — Windows: `SetConsoleMode` (disable line input/echo/processed input, enable virtual-terminal input; P/Invoke); Unix: termios raw via `tcgetattr`/`tcsetattr` P/Invoke (the ADR-0004 path — interactive input is our own VT layer, not `Console.ReadKey`). Never called when not interactive, so CI never touches a real terminal.

`SystemConsoleIO` (production) and `FakeConsoleIO` (tests: scripted `KeyEvent`s, recorded `Write` calls, scripted sizes and resize pushes) both live in `Lunate.Tui`; the fake is public for future frontends' tests? No — internal + `InternalsVisibleTo Lunate.Tui.Tests`.

**Fail-soft (ADR-0004):** a `ConsoleSupport.Check(IConsoleIO)` helper returns null or an actionable one-line diagnostic — Windows without a console (mintty with pseudo-console off, redirected handles): "use Windows Terminal or re-enable pseudo-console support"; stdout not a terminal or `TERM=dumb`: "interactive mode needs a terminal; use `lunate -p`". The interactive entry point (T-22) prints it and exits; the helper is tested now.

## VT input decoder

Port of the S-2 spike decoder (reference, not shipped code), productized as `VtInputDecoder`: incremental byte buffer; UTF-8 multi-byte; control bytes; `ESC`+char = Alt; CSI `A/B/C/D`, `H`, `F`, `Z`; SS3; tilde keys (`2~ 3~ 5~ 6~ 11~..24~`, xterm's non-contiguous numbering); modifier parameters (`1;5C`); bracketed-paste markers (`200~`/`201~` → paste start/end states); resync on unknown sequences. Key record: `KeyEvent(KeyKind Kind, string? Text, bool Ctrl, bool Shift, bool Alt)` — `Text` carries printable input and pasted text; paste arrives as `KeyKind.Paste` carrying the full text (markers stripped, `\r\n`/`\r` → `\n`).

**Escape disambiguation:** a lone `ESC` is ambiguous (it may begin a sequence). The reader holds it while more bytes arrive; when the stream has been quiet for a short window (75 ms), it emits `Escape`. The window runs on the injected scheduler in tests. The decoder itself stays pure (bytes in, events out, "need more bytes" as a result); the window lives in the reader.

## Input line

`InputLineState(string Text, int CursorPosition)` + pure operations: insert text (chars/paste), backspace, delete, left/right (by character, not byte), home/end, newline (`Ctrl+J` / `Alt+Enter` → buffer newline; terminals cannot detect `Shift+Enter`). Rendering: the buffer wraps/splits into display lines; cursor column computed with `CellText.Width` — a compact wcwidth-style helper (combining marks 0, East-Asian wide + emoji 2, default 1) with its own test table; the full Unicode table and grapheme clusters are a documented follow-up. No history, no completion, no key bindings beyond editing — T-21/T-22.

## Live area

- **State:** immutable `LiveAreaState` record — input line state, streaming tail (last committed text lines), active tool line (name + spinner), footer fields (model, tokens, context %, cwd, branch — values only in T-18; populated by T-22), size. Rendered from a snapshot; no shared mutable state (the S-5 readability finding).
- **Pipeline:** `Merge(keyStream, resized, spinnerPulse, [agentStream later])` → `Scan(initial, Reduce)` → `Sample(FrameInterval, scheduler)` → single render function → `FrameWriter` paints via `IConsoleIO.Write` with cursor-up/clear-line sequences; first paint reserves the area, exit clears it. `FrameInterval` = 33 ms (~30 fps).
- **Threading contract (written down, ADR-0007 condition):** exactly one writer paints the live area; the render callback is the only place that reads state, and it reads the sampled snapshot (state is read only after the pipeline has drained through `Sample`); producers only push stimuli into the pipeline. Documented in the spec and pinned by tests.
- **Spinner:** `Interval(120 ms, scheduler)` → frame index; sampled by the same 30 fps gate.
- **Tests:** `TestScheduler` drives everything time-based: the 30 fps cap (N stimuli inside one frame window → one paint), the spinner, the Esc window, resize polling. Frame goldens: `FakeConsoleIO` records frames; committed goldens under `tests/Lunate.Tui.Tests/fixtures/frames/`; `LUNATE_TUI_UPDATE_GOLDENS=1` rewrites them (documented; used deliberately, diffed in PRs).

## Files, tracking, hygiene

- `src/Lunate.Tui/`: `IConsoleIO.cs`, `ConsoleSize.cs`, `KeyEvent.cs`, `SystemConsoleIO.cs`, `Platform/UnixRawMode.cs`, `Platform/WindowsConsoleMode.cs`, `VtInputDecoder.cs`, `KeyReader.cs` (bytes → events, Esc window), `InputLine.cs`, `LiveArea.cs`, `LiveAreaState.cs`, `FrameWriter.cs`, `CellText.cs`, `ConsoleSupport.cs`; internals visible to `Lunate.Tui.Tests` (new `InternalsVisibleTo`).
- PublicAPI: `Lunate.Tui` is tracked — Unshipped gets the public surface (`IConsoleIO`, `ConsoleSize`, `KeyEvent`, `KeyKind`, `InputLine`(+state), `LiveArea`(+state), `ConsoleSupport`); implementation types internal.
- Culture: all readouts invariant; the de-AT suite pass applies; no `Thread.Sleep` anywhere (TestScheduler only).
- Startup: nothing references the TUI yet, so the exe budget is unchanged; ADR-0007's follow-up (re-measure with the live area in the path) lands when the TUI is wired (T-22).
- Windows Terminal + mintty manual check: maintainer hand-off, procedure modeled on S-2's; T-33 owns the full matrix.

## Deviations

(Filled during apply; empty at proposal time.)
