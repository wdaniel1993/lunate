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

- The keymap file lives at `~/.lunate/keymap.json` exactly as pinned, but the path is the
  Lunate home store (`~/.lunate`, moved wholesale by `LUNATE_HOME`), not the raw user profile:
  every other state file — sessions, settings, trust — follows the store, and the sandbox rule
  (a test or portable run pointed at `LUNATE_HOME` must not read the user's real configuration)
  has to hold for a file this session applies at startup. `$LUNATE_KEYMAP` and `--keymap` are
  unaffected. (Keymap, section 1, task 1.1.)
- "Fails the load" is implemented as the whole user file being discarded: the session starts
  with the shipped catalogue and one warning line naming why, never a partially applied file or
  a refused start. The next sentence ("a keymap it could not apply") pins that the binding
  resolving is all-or-nothing; "the session does not start" is the file, not the process, and a
  session-start refusal would be untestable at this change's tests' boundary.
- Application order: the persisted `/settings tui.keys` remaps apply first (as today), then the
  user file; a file entry beats a persisted remap on the same binding id. The design does not
  state the order; the file is the more explicit, more deliberate choice, so its entries stand.
- A conflict line names the consuming pair per contested key and context (the design's one
  line, `<a> and <b> both answer Enter in context Idle; <a> wins`); when the winner's keys take
  several keys in several contexts, the loser's line appears per key, and a loser that loses its
  last key is disabled rather than refused.

- The theme file lives at `~/.lunate/theme.json` exactly as pinned, but the path is the Lunate
  home store (`~/.lunate`, moved wholesale by `LUNATE_HOME`), not the raw user profile — the same
  reason the keymap deviation gives: every other state file follows the store, and a test or a
  portable run pointed at `LUNATE_HOME` must not read the user's real configuration. `--theme
  <path>` is unaffected. (Themes, section 2, task 2.2.)
- "Fails the load" is the whole theme file being discarded: the selected built-in keeps every
  token, and startup prints one warning line naming why. Never a half-applied file and never a
  refused start — the same rule the keymap follows, for the same reason. (Themes, task 2.2.)
- Selection when `--theme` names a file: the flag replaces the *file*, not the built-in, so the
  base theme remains the `tui.theme` setting (then `default`). A `--theme` that names a built-in
  still reads the home `theme.json` and overlays it, because that file is the user's choice of
  tokens rather than a second selection. (Themes, section 2, task 2.2.)
- The default theme's highlighted code maps to semantic tokens: a keyword takes `accent`, a JSON
  key `code`, a comment `muted`, a string `success`, a number and a shell variable `warning`. The
  ten tokens have no syntax-specific names, and a token whose meaning were a fixed colour would
  not survive a theme; the visible change is that a keyword is the accent's cyan rather than the
  old ANSI blue. The frames' goldens change only where this routing unifies a colour (the tool name
  and hunk header take the accent, and the sixteen-colour user echo takes `prompt`'s pinned
  fallback instead of plain "blue"). (Themes, task 2.1.)
- A colour keeps the form the theme declared it in: a named colour renders as that name at every
  depth (the byte sequence the palette's older, name-based spans emitted), a hex value is RGB on a
  true-colour terminal, the nearest 256 entry on a 256-colour one, and the nearest of the sixteen
  below that; a 256 index keeps its exact entry. The built-ins pin the sixteen-colour fallback the
  visual spec chose (a pastel hex has no honest Euclidean nearest — `#af87d7` resolves to silver),
  so `default` at sixteen colours paints the same values it painted before the tokens. (Themes,
  task 2.3.)
