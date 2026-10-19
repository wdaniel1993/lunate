# Design — narrow terminals

## D1. The breakpoint policy

Three bands, each a single predicate evaluated at render time from the terminal width:

- **Full (`width >= 100`)**: today's layout — every footer segment, full tool summaries, box rules, autocomplete descriptions.
- **Standard (`60 <= width < 100`)**: identical structure; the footer's own degradation (branch, then directory) applies; the input box rules hold at ≥80 and drop below 80 (existing).
- **Compact (`width < 60`)**: short tool headers, the compact footer `model · <pct>%`, no box rules, no autocomplete descriptions; diff gutters and markdown tables take their compact forms when those constructs exist.

The breakpoints are constants in the TUI (`NarrowWidth = 60`, `FullWidth = 100`) shared by the components, so a component never re-derives a magic number.

## D2. Short tool headers (compact band)

`ToolBlockRenderer.Header` gains the width: at `< 60` it renders glyph + name + meta — the summary is dropped:

- `read` — meta becomes the line count (`13 lines`; the summary carries path + range normally, so the count comes from the same result math the summary uses today).
- `bash` — meta is already `exit N · <duration>` (the command drops with the summary).
- `edit`/`write` — `+N −M` (already the meta).
- `cs_*` — the severity counts (already the meta).
- extension/MCP fallback — name only (the argument summary drops).

`ToolBlockMeta` gains the compact-band short meta for `read`; everything else reuses the existing meta. The header never wraps; it clips at the width as today.

## D3. The compact footer (compact band)

`StatusFooterRenderer.PlainText`: at `< 60` the segment list is `[model, "<pct>%"]` — the usage shrinks to the context percent (no tokens, no window), and think/directory/branch/widgets are dropped regardless of remaining width. The separator stays ` · `. The percent is omitted when the window is unknown (existing rule). At ≥60 today's behaviour is unchanged (append, then drop from the end while too wide).

## D4. The width invariant and resize

Every rendered line is clipped by `CellText.Clip` in the live area and the block renderers, so "nothing wider than the terminal" already holds by construction; this card pins it with a property-style test over the frame at each matrix size and with the composition test resizing the host (40 → 120 → 60) and asserting the frames re-render at each width. No new resize machinery: `LiveArea` already observes `IConsoleIO.Resized` and re-renders from the state; the test proves it end to end.

## D5. The snapshot matrix

New goldens at **40×20, 60×30, 80×24, 120×40** (width × height) for: an idle frame with a committed turn (tool blocks incl. a long summary), the input box (empty placeholder / filled), the autocomplete list (command mode, one selected), thinking, and the working indicator — each across truecolor/256/16/no-colour where colour matters. Heights are meaningful: the matrix pins that the live area plus scrollback fits the 20-row frame and the footer/box stay visible. Existing 120/80/50 × mode goldens stay.

## D6. Compact-band content rules for future constructs

Recorded so the polish card's additions bind here: the diff renderer SHALL drop its line-number gutter below 60 and markdown tables SHALL render as lists below 60 (and as tables at ≥60). Neither construct exists in the current renderers; when they land, the compact forms are part of their acceptance.

## D7. Tape and docs

A `features-narrow` tape at 40 columns driving the replay client (a committed turn with a compact header, the input box, `/` list without descriptions), GIF + README gallery. Guide: a "Narrow terminals" section with the three bands and the per-band rules.

## D8. Divergences and non-goals

- The `≥100` band's right-aligned footer segment (version · elapsed) is a footer-polish concern; the full band keeps today's one-line-left footer.
- The card names the bands as ≥100 / 60–99 / <60; the finer boundaries already live in the components (rules ≥80, descriptions ≥60) and stay.
- Tables and the diff gutter are not in the current subset; the compact rules bind when they arrive (D6).
- Startup budget: no new startup work; breakpoints are constants.
