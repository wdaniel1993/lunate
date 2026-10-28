# Design — TUI customization

Binding implementation detail. Where this file pins a behaviour, apply follows it verbatim; a
contradiction between this file and the spec delta is a stop-and-report, not a choice.

## 1. Keymap

- `src/Lunate.Tui/keys.json` remains the catalogue: every binding keeps its `id`, `intent`, `context`
  and `description`. Nothing in the shipped file changes shape.
- **User file: `~/.lunate/keymap.json`**, same top-level shape (`version`, `bindings`), read once at
  startup. `$LUNATE_KEYMAP` overrides the path; `--keymap <path>` overrides both, for testing.
- **Overlay by id.** A user binding with an existing `id` replaces that binding's `keys` and nothing
  else — intent, context and description stay the catalogue's. A user binding with a new `id` is
  rejected: a keymap cannot invent an intent, because intents are code.
- **Unknown id fails the load**, naming the id and up to three nearest catalogue ids (edit distance
  <= 3, sorted). The session does not start with a keymap it could not apply — a silently ignored
  binding is a user pressing a key that does nothing.
- **Conflicts**: two bindings that resolve to the same key in the same context with different intents
  are a conflict. The user's file wins over the catalogue, and startup prints one line:
  `keymap: <id-a> and <id-b> both answer Enter in context Idle; <id-a> wins`. Two *user* bindings in
  conflict fail the load instead — that is a typo, not a preference.
- **Empty `keys` array** disables the binding. That is how a user turns off a default without
  inventing a new intent.

## 2. Themes

- Built-in themes: `default` (today's palette), `ansi16` (named ANSI colours only, for 16-colour
  terminals) and `mono` (no colour, emphasis by weight only).
- **User file: `~/.lunate/theme.json`** maps semantic tokens to a colour. Tokens, and only these:
  `accent`, `muted`, `error`, `success`, `warning`, `code`, `diff.add`, `diff.remove`, `prompt`,
  `notice`. An unknown token fails the load with the list of valid tokens.
- A colour value is one of: an ANSI name (`red`, `bright-blue`), a 256-colour index (`colour: 208`),
  or a hex string (`#ff8800`). The renderer downgrades by capability: hex on a 256-colour terminal
  becomes the nearest 256 entry; on a 16-colour terminal the nearest of the 16; in a colourless
  terminal every token collapses to no colour. **`NO_COLOR` and the existing detection rules win over
  any theme file** — a theme cannot turn colour back on.
- Selection: `--theme <name|path>` > `tui.theme` setting > `default`.
- Themes never change glyphs. ASCII stand-ins remain the capability system's job.

## 3. Full screen

- Opt-in by `--fullscreen` or `tui.fullscreen: true`; **inline remains the default**.
- Entered after the first frame is painted (so a failure to enter still leaves a usable session),
  and left on every exit path, including `Ctrl+C`, an unhandled exception and a panic: the existing
  `AlternateScreen` helper owns enter/leave, and it is already exercised by the transcript viewer.
- While full screen is active the transcript keeps its scrollback inside the alternate screen; on
  exit the terminal's own scrollback is what it was before. No attempt is made to copy the transcript
  into it.

## 4. Mermaid diagrams

- **Grammar in scope**: `flowchart`/`graph` with `TD`/`TB`/`LR` direction, nodes with labels, edges
  with and without labels; `sequenceDiagram` with participants, `->`, `-->`, `->>` and `Note`.
  Everything else — subgraphs, styles, classes, links, click handlers, other diagram types — is
  *unsupported*, which means fallback, not failure.
- **Layout**: layered placement for flowcharts (BFS depth = row or column depending on direction),
  fixed column widths per box, edges drawn with the box-drawing set. Sequence diagrams: participant
  boxes on one row, lifelines down, message arrows with labels placed on the row below the arrow when
  the label fits, truncated with an ellipsis when it does not.
- **Width rule**: a diagram renders only if its widest line is <= terminal width and the terminal is
  >= 40 columns. Otherwise the fence falls back. The note is exactly one line:
  `mermaid: diagram is 74 columns wide, this terminal has 58 — showing the source`.
- **ASCII mode**: when the capability system reports no Unicode, the box glyphs become `+ - |` and
  arrowheads become `< > ^ v`. Same layout, no new geometry.
- **Parse failure**: the source renders as a code block plus one line `mermaid: <reason>`, the reason
  being the parser's one-line message with the line number. It never throws and never renders a
  partial diagram.
- **No new package.** The parser covers the grammar above; anything it does not recognise is
  *unsupported*, not *invalid*, so a future type degrades instead of erroring.

## Sign-off (2026-10-28)

The maintainer approved these additions, each binding on the apply:

- Unicode/box-drawing rendering only; no inline images in this release.
- Flowchart and sequence only, first.
- Unsupported type or too-wide diagram -> source in a code block plus a one-line note.
- Keybindings build on `keys.json`; full screen is an option, inline stays the default.

## Deviations

Filled by the apply run: every departure from this design, with the reason, before archive.
