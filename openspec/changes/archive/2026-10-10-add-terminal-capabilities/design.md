# Design: terminal capability detection and fallbacks (T-33)

## Structure

- `src/Lunate.Tui/TerminalCapabilities.cs` (new): detection record + logic.
- `src/Lunate.Tui/Markdown/MarkdownAstMapper.cs`: ASCII variants for bullets and quote prefixes.
- `src/Lunate.Tui/LiveArea.cs`: scrollback console colour system from the detected capabilities.
- `src/Lunate.Coding/InteractiveSession.cs`: detect once; steering echo prefix; pass capabilities to the renderer.
- Tests: `tests/Lunate.Tui.Tests/` (detection matrix, goldens), `tests/Lunate.Coding.Tests/` (echo fallback in the E2E or a focused test).

## Detection (pinned)

`TerminalCapabilities(ColorSystemSupport Color, bool Unicode)`; `Detect` takes injectable seams: `Func<string, string?> env` and `Func<int> outputCodePage` (default: real environment + Windows `GetConsoleOutputCP` P/Invoke; the delegate is never called on Unix).

- **Colour**: `NO_COLOR` set and non-empty → `NoColors`. Else `COLORTERM` containing `truecolor` or `24bit` (case-insensitive) → `TrueColor`. Else `ColorSystemSupport.Detect`. A non-interactive console → `NoColors` regardless (preserves today's behaviour).
- **Unicode**: `LUNATE_ASCII` set and non-empty → `false`; else `LUNATE_UNICODE` set and non-empty → `true` (the ASCII override wins when both are set — degrade is the safe direction). Else `TERM=dumb` → `false`. Else Windows: `WT_SESSION` set → `true`; else `outputCodePage() == 65001` → `true`; else `false`. Else Unix: effective locale = first non-empty of `LC_ALL`, `LC_CTYPE`, `LANG`; unset → `true`; contains `UTF-8`/`utf8` (case-insensitive) → `true`; else `false`.

## Consumers (pinned)

- **Markdown**: bullet glyph `• ` → `- `, blockquote prefix `│ ` → `| ` when Unicode is off. `MarkdownAstMapper` gains the capabilities parameter (explicit, no statics).
- **Steering echo**: `» ` → `> ` when Unicode is off (`InteractiveSession`).
- **Scrollback console**: `AnsiConsoleSettings.ColorSystem` = `NoColors` when non-interactive, else the detected colour (replaces the raw `Detect`).
- **Spinner**: stays ASCII (`| / - \`) by design — the guide's "ASCII spinner" fallback is satisfied structurally; if a Unicode spinner ever lands it must consult the capabilities. Documented, no code change.
- **Wiring**: `InteractiveSession` detects once at construction from its console + environment; `Cli` interactive entry keeps its existing gate (unchanged).

## Tests (pinned)

- Detection matrix via the seams: NO_COLOR (set/empty/unset), COLORTERM (truecolor/24bit/none), non-interactive override, Unicode branches (TERM=dumb; LUNATE_ASCII; LUNATE_UNICODE; both; Windows WT_SESSION; Windows code page 65001 vs 437; Unix locale UTF-8 vs `C` vs unset).
- Rendering goldens: a markdown document with bullets and a blockquote in ASCII mode (byte-exact `TestConsole` golden); the Unicode variant stays covered by existing goldens.
- Steering echo: focused test asserting `> ` in ASCII mode and `» ` otherwise (no new E2E snapshot needed unless cheap).
- No platform-dependent literal assertions (the seams carry the platform branches).

## Manual matrix (post-merge)

Checklist (from the guide's tier-1 table): Windows Terminal · conhost · VS Code terminal · Git Bash/mintty · macOS Terminal.app · iTerm2/Ghostty/WezTerm/Kitty/Alacritty (whichever are installed) · a Linux terminal · tmux/SSH. Per terminal: colours (true colour vs 256 vs none), Unicode vs ASCII rendering, resize, paste, `Option`-as-Meta note for Terminal.app. Results are recorded as comments on the kanban card (`t_bf81253f`), per the card's done-when.

## Deviations

1. `MarkdownRenderer` keeps its public parameterless constructor (Unicode on); the detected capabilities flow through a new internal constructor, so the public surface only gains `TerminalCapabilities` (the structure section named only `MarkdownAstMapper`).
2. `LiveArea` takes the capabilities as an optional constructor parameter (default: Unicode on, colour from Spectre's detection); the interactive session always passes the detected record.
3. `TerminalCapabilities.Detect` takes `isInteractive` and `isWindows` as explicit parameters beside the two pinned seams, so both platform branches are covered by tests on any OS.

## Seams

- Print mode glyphs (`»`, `×`, em dashes in messages) are out of scope — print output is consumed by pipes/CI where UTF-8 is the norm; revisit only if a real terminal report demands it.
- A Unicode spinner variant is future work, noted above.
