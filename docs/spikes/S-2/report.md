# S-2 report — raw-key input under Git Bash (mintty)

- Date: 2026-10-04
- Change: `run-phase0-spikes` (guide card T-03)
- Question: does raw-key reading work for a .NET console app inside mintty
  (Git Bash), and if not, what is the fallback?
- Method: throwaway console spike (`RawKeys/`, net10.0, outside `lunate.sln`),
  automated decode/probe checks on macOS, and a step-by-step manual check in a
  real Git Bash window on Windows.
- Status: **done.** Automated checks pass; the manual Windows run (2026-10-04)
  shows `Console.ReadKey` works **directly in mintty** on Git for Windows
  2.52.0, because the MSYS2 runtime hands native console apps a pseudo console
  (ConPTY). With pseudo-console support disabled (`MSYS=disable_pcon`) the
  historical failure returns. See [Manual results](#manual-results-windows-11-git-for-windows-2520).
- ADR draft: [`adr/0004-mintty-input-support.md`](../../../adr/0004-mintty-input-support.md)
  (proposed — awaiting maintainer sign-off).

## What was built

`RawKeys` is a single-file throwaway console app (no NuGet packages) with four
modes:

| Mode | What it does |
| --- | --- |
| `--selftest` | Runs 32 decoder cases (printable, control, Alt, CSI/SS3 arrows, function keys, modifiers, UTF-8, partial sequences) and exits non-zero on any failure. |
| `--probe` | Prints OS, `TERM`/`MSYSTEM`/`WT_SESSION`, `Console.Is*Redirected`, whether a Windows console is attached (`GetConsoleMode` on the stdin handle), and what `Console.KeyAvailable` does. |
| `--headless` | Decodes stdin bytes as VT input; used when stdin is redirected and for the automated check on any OS. |
| (no args) | Interactive: `Console.ReadKey(intercept: true)` in a loop, printing every key it sees; Esc or Ctrl+C exits. Falls back to headless when stdin is redirected, so the spike is safe to run in CI/shells without a TTY. |

The decoder (`KeyDecoder.cs`) maps VT input to `KeyEvent` records (name, char,
Ctrl/Shift/Alt, raw bytes): control bytes, `ESC`+char as Alt, CSI `A/B/C/D`,
`H/F`, `Z`, tilde keys (`2~ 3~ 5~ 6~ 11~..24~`), SS3 `P/Q/R/S`, and modifier
params (`1;5C` = Ctrl+Right). It was written test-first: the self-test table
existed first and failed 29/32 against a stub; the implementation then reached
32/32. The self-test caught one real bug (xterm skips 16/22, so `24~` is F12,
not a linear `F14`).

## Automated results (macOS arm64, .NET 10.0.103)

| Check | Command | Result | Evidence |
| --- | --- | --- | --- |
| Build | `dotnet build docs/spikes/S-2/RawKeys/RawKeys.csproj -c Release` | 0 warnings, 0 errors | — |
| Decoder self-test | `RawKeys --selftest` | 32/32 pass, exit 0 | [`evidence/selftest.txt`](evidence/selftest.txt) |
| Redirected VT input | `printf '…' \| RawKeys` | 37/37 bytes decoded: arrows, F1/F5/F12, Ctrl+Right, Ctrl+C, Alt+X, `ä`, Enter, Tab, Backspace, Delete; exit 0 | [`evidence/headless-redirected.txt`](evidence/headless-redirected.txt) |
| Plain text input | `printf 'hello' \| RawKeys --headless` | 5 keys, exit 0 | [`evidence/headless-plain-text.txt`](evidence/headless-plain-text.txt) |
| Empty input | `RawKeys < /dev/null` | `decoded_bytes: 0/0`, exit 0 | [`evidence/headless-empty.txt`](evidence/headless-empty.txt) |
| Probe without a TTY | `RawKeys --probe` | `stdin_redirected: True`; `Console.KeyAvailable` **throws** `InvalidOperationException: Cannot see if a key has been pressed when either application does not have a console or when console input has been redirected`; exit 0 | [`evidence/probe-macos.txt`](evidence/probe-macos.txt) |

The probe matters because it shows the exact failure class mintty has
historically triggered: when the process has no Windows console handle,
`Console.KeyAvailable`/`Console.ReadKey` throw `InvalidOperationException`.
The macOS run had no TTY, so it demonstrates that failure mode, not mintty
itself — mintty's result comes from the manual procedure below. The
`--headless` decode path is what a future Lunate input layer can rely on even
where `Console.ReadKey` is unavailable.

## Manual check procedure (Windows, real Git Bash/mintty window)

This is the task-3.2 run: it happens on the maintainer's Windows machine. The
commands are copy-pasteable from a **Git Bash (mintty)** window at the repo
root; `BIN` points at the Release build. Capture each step with `tee` into
`docs/spikes/S-2/evidence/` so the report can be completed.

Prerequisites: Windows + .NET 10 SDK; Git for Windows (record `git --version`
and `winpty --version`); this branch checked out.

**Step 0 — build and baseline info**

```bash
dotnet build docs/spikes/S-2/RawKeys/RawKeys.csproj -c Release
BIN=docs/spikes/S-2/RawKeys/bin/Release/net10.0/RawKeys.exe
git --version && (winpty --version || true) && echo "MSYSTEM=$MSYSTEM TERM=$TERM"
```

**Step 1 — probe directly in mintty**

```bash
"$BIN" --probe | tee docs/spikes/S-2/evidence/manual-mintty-probe.txt
```

Observe: `msystem` (e.g. `MINGW64`), `windows_console_attached`, and
`key_available`. `windows_console_attached: no` plus a `throws
InvalidOperationException` is the expected mintty failure.

**Step 2 — interactive directly in mintty**

```bash
"$BIN" 2>&1 | tee docs/spikes/S-2/evidence/manual-mintty-interactive.txt
```

Press, in order: `a`, `Shift+A`, `↑`, `↓`, `←`, `→`, `F1`, `F5`, `F12`,
`Ctrl+→`, `Alt+x`, `Backspace`, `Delete`, `PageUp`, then `Esc` to exit. Note
whether input is echoed, whether each press prints a `readkey key=…` line, and
whether the process exits `0` or prints `readkey-failed: …`.

**Step 3 — only if Step 2 fails: winpty**

```bash
winpty "$BIN" 2>&1 | tee docs/spikes/S-2/evidence/manual-winpty-interactive.txt
```

Repeat the same key sequence. `winpty` allocates a hidden console for the
native process; if this works, mintty support = "launch via winpty".

**Step 4 — ConPTY on/off**

Recent MSYS2 runtimes hand native console apps a pseudo console (ConPTY) by
default, so Steps 1–2 already exercise the ConPTY path and `MSYS=enable_pcon`
is a no-op. The useful variant is the opposite: disable it and repeat Steps
1–2 to see whether ConPTY is what makes mintty work.

```bash
MSYS=disable_pcon "$BIN" --probe | tee docs/spikes/S-2/evidence/manual-nopcon-probe.txt
MSYS=disable_pcon "$BIN" 2>&1 | tee docs/spikes/S-2/evidence/manual-nopcon-interactive.txt
```

**Step 5 — fallback terminal (control)**

Open **Windows Terminal** (Git Bash profile), repeat Steps 1–2, and record as
`manual-wt-*`. Also repeat in `cmd.exe`/`pwsh` as a known-good console control.
This is the environment Lunate will officially recommend if mintty fails.

**Step 6 — fill in the outcome table**

| Path | Expected if supported | Observed (2026-10-04) |
| --- | --- | --- |
| mintty direct (`RawKeys`, ConPTY on by default) | probe shows console attached, interactive prints correct keys, exit 0 | **supported**: console attached, 15/15 keys correct, exit 0 |
| mintty + `winpty` | same as above, under winpty | not run (direct works) |
| mintty + `MSYS=disable_pcon` | expected to fail (no console) | **fails**: stdin is a pipe, `GetConsoleMode` error 6, `KeyAvailable` throws, interactive falls back to headless |
| Windows Terminal (PowerShell tab) | same as direct | **supported**: console attached, 15/15 keys correct, exit 0 |

Outcome meanings:

- **Direct works with correct keys** → mintty is supported; record the Git for
  Windows version and no fallback is needed.
- **Direct fails, winpty works** → mintty is supported via the `winpty`
  launch mode; document it and keep Windows Terminal as the no-caveat path.
- **Direct fails, only Windows Terminal works** → mintty is not supported for
  the interactive TUI (shell integration still works); Lunate detects
  `MSYSTEM` and prints a one-line diagnostic pointing at Windows Terminal.
- **Keys arrive but mapped wrongly** (e.g. arrows mangled, modifiers lost) →
  partial support; record exact bytes (`--probe` plus a `--headless` pipe test
  with the bytes mintty sends) and treat as not supported until decoded.

Outcome: **direct works with correct keys**, on condition that the MSYS2
runtime's pseudo-console support is on (the default on the tested build).

## Manual results (Windows 11, Git for Windows 2.52.0)

Environment ([`evidence/manual-baseline.txt`](evidence/manual-baseline.txt)):
Windows 11 Pro 10.0.26200 x64, .NET SDK 10.0.201, Git for Windows
2.52.0.windows.1, MSYS2 runtime 3.6.5, winpty 0.4.3, `MSYSTEM=MINGW64`,
`TERM=xterm`, `TERM_PROGRAM=mintty`.

| Run | stdin | `GetConsoleMode` | `Console.KeyAvailable` | Interactive `ReadKey` | Evidence |
| --- | --- | --- | --- | --- | --- |
| mintty direct | console | succeeds | `no` (returns, no throw) | 15/15 keys correct (incl. `Shift+A`, `Ctrl+→` = `RightArrow`+`Control`, `Alt+x`, F1/F5/F12), exit 0 | [`probe`](evidence/manual-mintty-probe.txt), [`interactive`](evidence/manual-mintty-interactive.txt) |
| mintty, `MSYS=disable_pcon` | pipe (`stdin_redirected: True`) | fails, win32 error 6 (invalid handle) | throws `InvalidOperationException` | not reachable; the spike falls back to headless, and mintty's pty stays in cooked mode (echoes typed bytes, delivers only on Enter). Run aborted with Ctrl+C | [`probe`](evidence/manual-nopcon-probe.txt), [`interactive`](evidence/manual-nopcon-interactive.txt) |
| Windows Terminal, PowerShell tab | console (`WT_SESSION` set) | succeeds | `no` (returns, no throw) | 15/15 keys correct, exit 0 (one extra `Alt+a` line is an operator misclick, not a decode error) | [`probe`](evidence/manual-wt-probe.txt), [`interactive`](evidence/manual-wt-interactive.txt) |

Notes:

- `stdout_redirected: True` in the probes comes from piping into `tee`, not
  from the terminal.
- Terminal echo next to the `readkey` lines was not explicitly recorded; the
  captured output contains only the spike's own lines.
- Windows Terminal was run with a PowerShell tab (no Git Bash profile was
  configured); it is the same console host either way.
- winpty (Step 3) was not run because direct mintty works.

## Fallback

mintty works directly when the MSYS2 runtime provides a pseudo console, so no
launch wrapper is needed on current Git for Windows. The fallback covers the
case where it does not (pseudo console disabled via `MSYS=disable_pcon`, or an
older runtime without it): the app must fail soft — detect the no-console
condition (`GetConsoleMode` fails on stdin / `Console.IsInputRedirected` while
`MSYSTEM` is set) and print "this terminal cannot deliver raw keys; use
Windows Terminal, or re-enable pseudo-console support" instead of crashing with
an unhandled exception. **Windows Terminal** stays the no-caveat path. The
proposed decision is in [ADR 0004](../../../adr/0004-mintty-input-support.md).

## Surprises

- `Console.KeyAvailable` throws the same `InvalidOperationException` on any
  redirected stream, so the "mintty problem" can be reproduced and tested
  without mintty — the probe is one command and works on every OS.
- The interactive path in .NET (`Console.ReadKey`) and the VT decode path are
  different input mechanisms; only the latter can survive a terminal that is
  not a Windows console. The spike exists to measure the former before the
  TUI (T-18) commits to the latter.
- xterm's function-key numbering is not linear: `16~` and `22~` are skipped, so
  `24~` is F12. The self-test caught the linear `F{code-10}` bug (F12 showed
  up as F14) before it could reach the report.
- The "mintty problem" is effectively solved upstream: the MSYS2 runtime's
  pseudo-console support gives native apps a real console, and `ReadKey`
  decodes every tested key under mintty exactly as under Windows Terminal.
  Turning it off reproduces the historical failure one-for-one, which pins the
  dependency precisely.

## Limits

- The manual run covers one machine and one Git for Windows build (2.52.0,
  MSYS2 runtime 3.6.5). Older builds were not tested; which version first
  enabled pseudo-console support by default was not verified, so the fail-soft
  path is what covers them.
- Only the keyboard path through `Console.ReadKey` was tested under mintty;
  the VT decoder was not fed mintty's raw bytes (not needed, since `ReadKey`
  works).
- Windows Terminal ran a PowerShell tab, not a Git Bash profile.
- The decoder covers keyboard input only (no mouse reporting, bracketed
  paste, or kitty keyboard protocol); it is a probe, not the TUI input layer.
- The macOS probe ran without a TTY; it demonstrates the no-console failure
  class, not mintty's exact environment.
- Spike code is throwaway and lives outside `lunate.sln`; nothing here changes
  `src/`.
