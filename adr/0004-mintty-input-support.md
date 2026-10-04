# 0004 — Windows terminals: Windows Terminal and Git Bash (mintty) supported, fail soft without a console

- Status: proposed — awaiting maintainer sign-off (S-2 manual run done 2026-10-04)
- Date: 2026-10-04
- Spike: `docs/spikes/S-2/report.md` (automated and manual evidence under `docs/spikes/S-2/evidence/`)

## Context

Lunate is a terminal TUI and its input line (T-18) reads VT raw keys. The
guide's Windows terminal matrix wants Git Bash (mintty) to work, but mintty
does not allocate a Windows console for native console processes: historically
`Console.ReadKey`/`Console.KeyAvailable` fail there with
`InvalidOperationException` ("…does not have a console…"). The guide asks for
an early test and a documented fallback (Windows Terminal).

S-2 built a throwaway probe (`docs/spikes/S-2/RawKeys`) with a headless VT
decoder, a console-capability probe, and a `Console.ReadKey` interactive mode.
Automated macOS evidence (no TTY) reproduces the failure class: when stdin is
not a console, `Console.KeyAvailable` throws that exact exception, while the
VT decoder still handles redirected input deterministically (32/32 self-test,
37/37 bytes across arrows, function keys, modifiers, and UTF-8). The manual
check on a real Git Bash (mintty) window on Windows 11 (Git for Windows
2.52.0, MSYS2 runtime 3.6.5) found:

- **mintty direct**: the process gets a real Windows console (the MSYS2
  runtime's pseudo-console/ConPTY support is on by default); `Console.ReadKey`
  decoded 15/15 test keys correctly, including Shift, Ctrl and Alt modifiers
  and F1/F5/F12; exit 0.
- **mintty with `MSYS=disable_pcon`**: stdin is a pipe, `GetConsoleMode`
  fails, `Console.KeyAvailable` throws — the historical failure, reproduced.
- **Windows Terminal**: identical to mintty direct.

`winpty` was not needed and not run.

## Decision (proposed)

- **Interactive input is read through Lunate's own VT input layer, not
  `Console.ReadKey`.** The spike's `ReadKey` mode exists to measure the legacy
  API; the product path must tolerate terminals that have no Windows console
  (T-18 already plans VT raw keys).
- **Windows Terminal is the primary supported Windows terminal.** It is the
  guide's default and the control environment for the manual check.
- **Git Bash (mintty) is supported, launched directly**, on Git for Windows
  builds whose MSYS2 runtime provides a pseudo console (verified on 2.52.0).
  No `winpty` wrapper is documented. Shell integration (`bash.exe`
  invocation) is unaffected.
- **Fail soft, always**: when no console is attached (pseudo console disabled,
  older runtime, or redirected stdin), the TUI detects it and prints a
  one-line diagnostic — use Windows Terminal, or re-enable pseudo-console
  support — rather than throwing.

## Alternatives considered

- **Depend on `Console.ReadKey` as the input API**: rejected — it needs a real
  Windows console, fails under mintty and under any redirected stdin, and
  cannot be the base of the TUI's raw-key handling.
- **Drop Git Bash/mintty support entirely**: rejected — Git Bash is the guide's
  first-choice Windows shell and users will run the TUI from it; a hard "no"
  is worse than a guided fallback.
- **Require `winpty` for mintty**: rejected — extra moving part with
  deprecation risk, and unnecessary since direct launch works under the
  default pseudo console.

## Consequences

- The TUI input layer owns VT decoding and terminal-capability detection; the
  spike's decoder is a reference, not shipped code.
- The guide's manual terminal matrix records mintty as supported (direct,
  pseudo console required) with Git for Windows 2.52.0 as the verified build.
- Users on a terminal without a console (mintty with pseudo console off, older
  runtimes) get an actionable diagnostic instead of a crash.
- Re-open if a Git for Windows release turns pseudo-console support off by
  default, or if users report mintty key mapping differing from Windows
  Terminal.
