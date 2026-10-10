# Tasks: terminal capability detection and fallbacks (T-33)

## 1. Detection (Tui)

- [x] 1.1 `TerminalCapabilities` record + `Detect` with env and code-page seams; colour rules (NO_COLOR, COLORTERM, Spectre, non-interactive)
- [x] 1.2 Unicode rules (LUNATE_ASCII/LUNATE_UNICODE, TERM=dumb, Windows WT_SESSION/code page, Unix locale) + the Windows `GetConsoleOutputCP` P/Invoke
- [x] 1.3 Detection-matrix tests over the seams (every branch, incl. both overrides set)

## 2. Fallback wiring

- [x] 2.1 `MarkdownAstMapper` ASCII variants (`• ` → `- `, `│ ` → `| `) via the capabilities parameter
- [x] 2.2 Steering echo `» ` → `> ` in ASCII mode (InteractiveSession)
- [x] 2.3 Scrollback console colour from the detected capabilities (non-interactive stays colourless)
- [x] 2.4 ASCII golden (markdown bullets + blockquote through the test console) + echo fallback test

## 3. Gate

- [x] 3.1 `bash scripts/verify.sh` green; deviations recorded in design.md
