## ADDED Requirements

### Requirement: Terminal capability detection and fallbacks

The TUI SHALL detect terminal capabilities once per session and degrade every capability to a tested fallback. Colour depth SHALL come from `NO_COLOR` (set and non-empty disables colour everywhere), `COLORTERM` (`truecolor` or `24bit` → true colour) and Spectre's detection, with non-interactive output always colourless. Unicode support SHALL come from `TERM=dumb` (off), Windows (`WT_SESSION` set, or the console output code page 65001) and Unix locales (UTF-8 in the effective `LC_ALL`/`LC_CTYPE`/`LANG`; on when unset), overridable via `LUNATE_ASCII` (force off; wins when both overrides are set) and `LUNATE_UNICODE` (force on). Without Unicode the renderer SHALL use ASCII stand-ins for every decorative glyph — markdown bullets `• ` → `- `, blockquote prefixes `│ ` → `| `, the steering echo prefix `» ` → `> ` — and the spinner stays ASCII by design. Detection SHALL take injectable environment and code-page seams so every branch is covered by tests.

#### Scenario: NO_COLOR disables colour
- **GIVEN** `NO_COLOR` set and non-empty
- **WHEN** capabilities are detected
- **THEN** the colour system is colourless even when `COLORTERM` claims true colour

#### Scenario: COLORTERM upgrades the colour depth
- **GIVEN** `NO_COLOR` unset and `COLORTERM=truecolor`
- **WHEN** capabilities are detected
- **THEN** the colour system is true colour

#### Scenario: Non-interactive output is always colourless
- **GIVEN** a non-interactive console
- **WHEN** capabilities are detected
- **THEN** the colour system is colourless regardless of the environment

#### Scenario: Unicode is detected per platform
- **GIVEN** `TERM=dumb`, or Windows without `WT_SESSION` and a non-UTF-8 code page, or a Unix `C` locale
- **WHEN** capabilities are detected
- **THEN** Unicode support is off; with `WT_SESSION`, code page 65001, a UTF-8 locale, or no locale set it is on

#### Scenario: The overrides win
- **GIVEN** `LUNATE_ASCII` or `LUNATE_UNICODE` set
- **WHEN** capabilities are detected
- **THEN** the override decides, and `LUNATE_ASCII` wins when both are set

#### Scenario: ASCII mode replaces every decorative glyph
- **GIVEN** Unicode support off and a document with bullets and a blockquote
- **WHEN** it renders, and when a steering message is echoed
- **THEN** the bullets render as `- `, the blockquote as `| `, the echo as `> `, and the output matches the committed golden

## MODIFIED Requirements

### Requirement: Steering echo

When the harness injects a steering message (`SteeringInjected`), the session SHALL commit a dim echo block — `» <text>`, or `> <text>` when Unicode support is off — to scrollback, in injection order; steering returned to the input line (Esc, error, step limit) SHALL never be echoed.

#### Scenario: Injected steering is echoed

- **GIVEN** a running turn with a queued steering message
- **WHEN** the message is injected before the next model call
- **THEN** the scrollback gains the dim echo, in order

#### Scenario: Returned leftovers are not echoed

- **GIVEN** queued steering that the user takes back with Esc
- **WHEN** the run ends
- **THEN** no echo for it appears in scrollback
