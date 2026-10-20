# Design — input-affordance

## Context

Before `bd22160`, `LiveAreaRenderer` drew both rules only from 80 columns (`RuleMinWidth = 80`). At 40 and 50 columns the live area was a bare `>` line above the footer, so an empty prompt read as ordinary output. The commit split the rules: the upper rule is unconditional, the lower rule uses `LowerRuleMinWidth = WidthBands.NarrowWidth` (60), and the prompt marker and placeholder moved from `LivePalette.Muted` to a new `LivePalette.Prompt` tone. The change is local to the live-area renderer and palette; no container or component boundary moves, so no C4 diagram is warranted.

## Decisions

### D1 — Rule bands

| Width | Upper rule | Lower rule | Footer | Prompt tone |
| --- | --- | --- | --- | --- |
| 40 | yes | no | compact `model · <pct>%` | `user` |
| 50 | yes | no | compact `model · <pct>%` | `user` |
| 60 | yes | yes | standard (degrading) | `user` |
| 80 | yes | yes | standard (degrading) | `user` |
| 120 | yes | yes | full | `user` |

The upper rule is unconditional, so the input always has a top edge. The lower rule shares the 60-column boundary with the compact band (`WidthBands.NarrowWidth`); at 60–99 the standard band keeps both rules, and the old 80-column boundary disappears. No component re-derives the number.

### D2 — Palette

`#87afd7` is the visual spec's dark `user` value and was already the committed user echo (`UserEchoRenderer.DarkHex`); it is now shared as `LivePalette.PromptHex`. Capability mapping: truecolour `\x1b[38;2;135;175;215m`, 256-colour `\x1b[38;5;110m` (xterm 110), 16-colour `\x1b[34m` blue, `NO_COLOR` no attribute. An empty input paints the prompt marker and placeholder as one span; a typed input paints only the `>` marker, leaving the text and the two-space continuation indent plain — the same separation as the committed user block. This is not a new palette decision: ADR-0019 keeps the product's own TUI stack, and the extension reuses its theme's `user` tone.

### D3 — Before and after

Before: at 40/50 columns no rule, so an empty input looked like output; the placeholder was muted `#8a8a8a`; 60–79 had no rules; from 80 both rules. After: the upper rule at every width, the lower rule from 60, and the prompt tone on the marker and placeholder at every width. The compact band gains one line of chrome; the live-area budget already counted the upper rule as a fixed prefix, so the input keeps its eight lines and the footer stays visible (pinned by the 40×20 goldens).

## Risks / Trade-offs

- [Compact frames lose one more row to chrome] → the upper rule is part of the fixed prefix; the eight-line input and footer still fit, and the 40×20 goldens pin it.
- [16-colour blue is weaker than the 256-colour tone] → the `>` marker carries the affordance in every colour mode and `NO_COLOR` stays plain; colour never carries meaning alone.
- [A theme resolver may later move the value] → the constant lives in `LivePalette` beside the other theme tones, so the move is one file.

## Open Questions

- None; the visual spec's `user` value is the only new tone and the bands reuse `WidthBands.NarrowWidth`.
