# TUI customization: themes, keymap, full screen and diagrams

## Problem

The terminal interface is the product, and it is fixed. Four gaps, all of them the same complaint from
different angles — the interface cannot be adapted to the person using it:

1. **No keymap.** `src/Lunate.Tui/keys.json` already lists every binding with an `id`, an `intent`, a
   `context` and a description, but it is shipped data only: a user who wants `Ctrl+J` to submit, or
   `Esc` to clear instead of interrupt, has no supported way to say so. The catalogue exists; the
   override does not.
2. **No themes.** Colours come from the capability detection and a single palette. A user on a light
   terminal, a mono terminal or one with a different colour sense of "danger" has no way to change it
   without rebuilding.
3. **No full screen.** The transcript viewer opens an alternate screen; the session itself never
   does, so long answers scroll the input away on a small terminal.
4. **No diagrams.** A ```mermaid fence renders as source. Answers that would be clearer as a
   flowchart or a sequence are the ones this product is most likely to produce.

## What changes

One change, four surfaces, delivered in parts — each part is independently reviewable and independently
useful, and no part depends on a part that lands after it:

- **Keymap** — a user file that overlays the shipped catalogue by binding id.
- **Themes** — named built-in themes plus a user token file, downgraded by the existing capability
  detection rather than by a second colour system.
- **Full screen** — an opt-in alternate screen for the session, inline remaining the default.
- **Diagrams** — a Mermaid renderer for the two diagram types we can draw well, with a defined
  fallback for everything else.

## Pinned decisions (maintainer sign-off, 2026-10-28)

- **Mermaid: Unicode and box-drawing only.** Inline images (kitty, iTerm2, sixel) are out for this
  release; a terminal without a graphics protocol is the normal case, not the exception.
- **Flowchart and sequence first.** Other diagram types are not attempted; an unsupported type is not
  an error, it is a fallback.
- **Unsupported type, or a diagram wider than the terminal, renders the source in a fenced code block
  with a one-line note.** Never a broken drawing, never a silent truncation.
- **Keybindings build on `keys.json`** — the shipped file stays the catalogue; the user file overlays
  it by id.
- **Full screen is an option; inline stays the default.**

## Alternatives considered

- **A plugin/extension renderer for diagrams.** Rejected for this change: an extension cannot draw into
  the transcript at the point a fence appears, so the seam would be invented for one consumer. If a
  second consumer appears later, the renderer behind it is already a single interface.
- **A Mermaid parser dependency.** Rejected: the two supported types are a bounded grammar, and the
  repo's rule is that rendering primitives are in-product (the syntax highlighters are). A dependency
  that pulls a browser-oriented parser into a terminal agent buys types we are refusing to draw.
- **A theme file that lists raw ANSI codes per widget.** Rejected: it makes every theme a function of
  the widget set, so a new widget breaks every theme. The file maps *semantic tokens* instead.
- **Full screen as the default.** Rejected: it breaks scrollback, which is how people read a terminal
  agent's output today.

## Impact

- Capability `tui`: one requirement modified (Markdown subset rendering), four added.
- Code: `src/Lunate.Tui` (keymap loader, theme tokens, alternate-screen session mode, Mermaid
  renderer), `Lunate.Coding` wiring for the flags and settings, docs under `docs/`.
- No new package references. No change to the wire protocol, the agent loop, or print mode: every
  surface here is TUI-only and inert in print/ACP runs.

## Out of scope

- Inline images of any protocol, third-party theme galleries, a plugin API for renderers, Mermaid
  types beyond flowchart and sequence (state, class, ER, gantt, pie, git graphs), and theme editing
  inside the TUI.
