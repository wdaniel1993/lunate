# Narrow terminals — breakpoints, resize, snapshot matrix

## Why

Lunate's TUI is designed at 120 columns but has to work in a split pane on a laptop and a phone terminal (Termux on ~40×20). Several width rules already landed with the block forms, the viewer and the input box (rules drop below 80, autocomplete descriptions below 60), but the two compact-terminal behaviours from the visual spec are missing: below 60 the tool headers still carry their full summaries, and the footer still renders its full segment list (dropping segments until it fits, rather than switching to the compact form). The snapshot matrix also stops at 50 columns wide and never varies height, so a height-sensitive regression (scrollback + live area at 20 rows) is invisible to the suite.

## What changes

- **The breakpoint policy** becomes explicit and tested: `≥100` full, `60–99` standard, `<60` compact.
- **Below 60 columns** — tool headers shorten to glyph + name + meta (`✓ bash · exit 0 · 0.4s`, `✓ read · 13 lines`, `✓ edit · +5 −1`; the summary drops), the footer becomes `model · <pct>%` (tokens and window collapse to the percent), the box rules stay off (≥80 rule), and autocomplete descriptions stay off (≥60 rule). When the diff gutter and markdown tables land in the polish card, their compact forms (no gutter, tables as lists) bind at this breakpoint — recorded as a constraint, not built here.
- **Nothing renders wider than the terminal**, and every resize re-renders: the live area already subscribes to the console's resize signal and state is width-driven, so this card pins the invariant with a test rather than adding machinery.
- **Snapshots at 40×20, 60×30, 80×24, 120×40** across the states and colour modes, plus a real-host composition test that resizes mid-session and asserts the re-render.

## Scope

- The footer's compact form and the tool headers' short form; the width invariant; the snapshot matrix; a tape showing a narrow terminal; docs.
- The diff gutter and markdown tables are not part of the current markdown subset/block renderer — their compact rules are recorded as binding constraints for the card that adds them.
- The `≥100` band's right-aligned footer segment (version · elapsed) is a footer-polish item, not a narrow-terminal rule; it is out of scope here.
- No new packages.

## Acceptance

Snapshot tests at the four sizes per breakpoint and colour mode; a production composition test that resizes the real host; a VHS tape driven by the replay client at a 40-column tape; the startup budget unchanged. OpenSpec change per the design; the kanban card links to it.
