# Tasks — narrow terminals

## 1. The compact band (<60)

- [x] 1.1 Breakpoint constants (`NarrowWidth = 60`, `FullWidth = 100`) in the TUI with a shared helper; unit tests pin the bands.
- [ ] 1.2 Short tool headers: `ToolBlockRenderer.Header` takes the width and renders glyph + name + meta at <60; `ToolBlockMeta` gains the read line-count short meta; unit tests per tool (read/bash/edit/cs/fallback) at 59 and 60.
- [ ] 1.3 The compact footer: `StatusFooterRenderer.PlainText` returns `model · <pct>%` at <60 (tokens/window/think/dir/branch/widgets dropped; percent omitted when the window is unknown); unit tests at 40/59/60/80.
- [ ] 1.4 ASCII/Unicode: the short headers keep the existing glyph and separator fallbacks; a no-Unicode compact header test.

## 2. The width invariant and resize

- [ ] 2.1 A frame invariant test: at 40×20, 60×30, 80×24, 120×40 every rendered line's cell width ≤ the terminal width (live area + a committed-turn scrollback frame with long summaries/diffs).
- [ ] 2.2 The composition test resizes the real host mid-session (40 → 120 → 60) and asserts the frames re-render at each width with the band-appropriate rules (short headers and the compact footer at 40, full headers at 120).

## 3. The snapshot matrix

- [ ] 3.1 New goldens at 40×20, 60×30, 80×24, 120×40 for: idle + committed turn (blocks with a long summary), input box empty/filled, autocomplete command list with a selection, thinking, working indicator — × truecolor/256/16/no-colour where colour applies.
- [ ] 3.2 Deterministic (fixed durations/clock as in the existing golden tests); existing 120/80/50 matrices stay green.

## 4. Media and docs

- [ ] 4.1 `docs/media/tapes/features-narrow.tape` at 40 columns driven by the replay client (compact header, input box, `/` list without descriptions); GIF + README gallery entry.
- [ ] 4.2 `docs/guide.md`: a "Narrow terminals" section with the three bands and their rules (short headers, compact footer, the existing ≥80/≥60 boundaries), plus the constraint note for the future diff gutter/tables.
- [ ] 4.3 Startup budget unchanged — verified by the change gate.
