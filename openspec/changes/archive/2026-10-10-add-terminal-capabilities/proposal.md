# Proposal: terminal capability detection and fallbacks (T-33)

## Why

The guide promises "every capability has a fallback" and lists the exact detection sources (Spectre + `COLORTERM`, `NO_COLOR`, Unicode, widths). Widths (`CellText`), the ASCII spinner and the interactive gate already exist, but there is no explicit, testable capability detection: `NO_COLOR`/`COLORTERM` are never consulted by our code, Unicode support is never detected, and the decorative glyphs (markdown bullets/quotes, the steering echo prefix) have no ASCII stand-ins. T-33 adds the detection module and wires every fallback, with seams so every branch is tested in CI.

## What changes

- **`TerminalCapabilities` (Lunate.Tui)**: one detection per session — colour depth from `NO_COLOR` / `COLORTERM` / Spectre's detection (non-interactive always colourless), Unicode support from `TERM=dumb`, Windows (`WT_SESSION`, output code page 65001), Unix locales (UTF-8 in the effective locale), with `LUNATE_ASCII` / `LUNATE_UNICODE` overrides. Injectable environment and code-page seams for tests.
- **Fallback wiring**: markdown bullets `• ` → `- `, blockquote `│ ` → `| `, steering echo `» ` → `> ` when Unicode is off; the scrollback console takes the detected colour system (non-interactive stays colourless); the spinner is ASCII by design (documented).
- **Tests**: detection matrix over the seams (env/code-page variants), NO_COLOR/COLORTERM behaviour, ASCII-rendering goldens, steering echo fallback.
- **Manual matrix (post-merge)**: the tier-1 terminal checklist (guide table) run by the maintainer; results noted in the kanban card, per the card's done-when.

## Done when

Every detection branch has a test; ASCII goldens cover the glyph fallbacks; `scripts/verify.sh` green; the manual matrix is checked on the tier-1 terminals and noted in the card.
