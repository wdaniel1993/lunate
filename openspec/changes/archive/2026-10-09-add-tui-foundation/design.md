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

- `src/Lunate.Tui/`: `IConsoleIO.cs`, `ConsoleSize.cs`, `KeyEvent.cs`, `SystemConsoleIO.cs`, `Platform/UnixRawMode.cs`, `Platform/TermiosLayout.cs`, `Platform/WindowsConsoleMode.cs`, `VtInputDecoder.cs`, `KeyReader.cs` (bytes → events, Esc window), `InputLine.cs`, `LiveArea.cs`, `LiveAreaState.cs`, `FrameWriter.cs`, `CellText.cs`, `ConsoleSupport.cs`; internals visible to `Lunate.Tui.Tests` and to `lunate` (the `Lunate.Coding` assembly name, which wires the TUI at T-22).
- PublicAPI: `Lunate.Tui` is tracked — Unshipped gets the Rx-free public surface (`IConsoleIO`, `ConsoleSize`, `KeyEvent`, `KeyKind`, `InputLine`(+state), `ConsoleSupport`) and nothing else. `LiveArea`/`LiveAreaState` are internal precisely so `IScheduler` never appears in a public signature (ADR-0007: "Rx types are not part of Lunate's public API"); a reflection test pins the exported set and forbids `System.Reactive` types in it.
- Culture: all readouts invariant; the de-AT suite pass applies; no `Thread.Sleep` anywhere (TestScheduler only).
- Startup: the `Lunate.Coding` → `Lunate.Tui` project reference pre-exists, but no `.cs` file calls into the TUI yet, so no Tui/Rx type is loaded and the exe budget is unchanged; ADR-0007's follow-up (re-measure with the live area in the path) lands when the TUI is wired (T-22).
- Windows Terminal + mintty manual check: maintainer hand-off, procedure modeled on S-2's; T-33 owns the full matrix.

## Deviations

- **Files added beyond the list:** `src/Lunate.Tui/FakeConsoleIO.cs` (the fake was described but not listed) and `src/Lunate.Tui/LiveAreaRenderer.cs` (the pure state→frame function split out of `LiveArea` to keep files small). `InputLineLayout` is internal and lives in `InputLine.cs`.
- **Testability seams for the fail-soft and poll paths:** `ConsoleSupport.Describe(console, isWindows)`, `SystemConsoleIO.ComputeIsInteractive(...)` and `SystemConsoleIO.PollSize(...)` are internal statics so every branch is covered with `TestScheduler` without ever constructing `SystemConsoleIO` in tests; the public surface stays as listed.
- **KeyEvent text for control letters:** `Ctrl+<letter>` is `KeyKind.Character` with lowercase `Text` (e.g. `Ctrl+J` → `Character "j", Ctrl: true`), not the S-2 spike's display `Name`; the 32-case table was ported with the new record shape and identical semantics.
- **LiveArea inputs:** `PostKey`/`AppendTail`/`SetTool`/`SetFooter` are the public stimulus methods (T-22 wires `ReadKeysAsync` through `PostKey`); `Start()` also starts the console key pump. Tests drive the methods directly so virtual time stays deterministic.
- **Frame goldens encode the recorded `IConsoleIO.Write` payloads** (one escaped line per write: `\x1b`, `\r`, `\n`, `\t`, doubled backslash) instead of logical frame lines; regeneration via `LUNATE_TUI_UPDATE_GOLDENS=1` is documented in `FrameGoldenTests`.
- **`Skip(1)` after `StartWith(initial)`** in the pipeline drops the synthetic seed so the idle spinner interval cannot repaint the initial frame; the first real state change still flows to `Sample`.
- **Per-platform termios layouts:** `Platform/TermiosLayout.cs` (a file beyond the list) holds explicit macOS and Linux tables. Darwin's 8-byte `tcflag_t` with `NCCS 20` puts `c_lflag` at 24 and `c_cc` at 32 (VMIN 48, VTIME 49); Linux's 4-byte flags plus the one-byte `c_line` put `c_lflag` at 12 and `c_cc` at 17 (VMIN 23, VTIME 22). Header sources are cited in the file (XNU `bsd/sys/termios.h`; glibc `bits/termios-struct.h`, `bits/termios-c_cc.h`, `bits/termios-c_iflag.h`, `bits/termios-c_lflag.h`). Real-terminal raw-mode behavior is deferred to T-33's manual matrix; the layouts are pinned by `UnixRawModeTests`, which run no P/Invoke.
- **cfmakeraw input set:** raw mode clears `IGNBRK|BRKINT|PARMRK|ISTRIP|INLCR|IGNCR|ICRNL|IXON` (not only `IXON`), so Enter keeps arriving as CR and the decoder's deliberate 0x0D/0x0A distinction holds; the per-platform mask is pinned by `UnixRawModeTests`.
- **`LiveArea`/`LiveAreaState` are internal:** `IScheduler` is injected but never public (ADR-0007), `InternalsVisibleTo` grants `lunate` (Lunate.Coding) access for T-22, and `PublicApiTests` pins the exported set and rejects `System.Reactive` types in it; PublicAPI.Unshipped.txt carries only the Rx-free surface.
- **Windows console-mode failures are checked:** entering raw mode throws `InvalidOperationException` carrying `GetLastWin32Error()` when VT input/output cannot be enabled, and `Restore.Dispose` also surfaces a failed restore (leaving the user's terminal raw is the worse outcome). The P/Invoke path remains covered only by T-33's manual matrix.
- **`Dispose` schedules the final clear:** `LiveArea.Dispose` cancels the key pump, disposes the pipeline subscriptions (so the render callback can no longer run), then queues `FrameWriter.Clear` on the injected scheduler instead of clearing from the disposing thread, and disposes the `CancellationTokenSource`; `Dispose_with_a_pending_render_schedules_exactly_one_clear` pins the single deterministic clear.
