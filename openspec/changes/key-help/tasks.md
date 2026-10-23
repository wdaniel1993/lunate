# Tasks — in-app key help

## 1. Model

- [ ] 1.1 `KeyPattern.Describe()`: render `Ctrl+`, `Alt+`, `Shift+`, named keys and bare characters.
- [ ] 1.2 `KeyHelpModel`: rows projected from `KeyBindingCatalog.All`, bucketed by context, with
      `Here`/`All` scope, a case-insensitive filter and a clamped selection.
- [ ] 1.3 Unit tests: description formatting, bucketing, filter, scope switching.

## 2. Overlay

- [ ] 2.1 `KeyHelpRenderer`: title, filter line, bucketed rows, windowed selection, empty state.
- [ ] 2.2 Session state + key handling: `?` opens on an empty idle prompt, `j`/`k`/arrows move,
      `/` filters, `Tab` switches scope, `Esc`/`?` close; every other key is ignored while open.
- [ ] 2.3 `Esc` and `?` close without touching the draft; `/` never inserts into the prompt.

## 3. Hint

- [ ] 3.1 `StatusFooterRenderer` renders a dim `? keys` beside the mode hint when idle and the
      prompt is empty. Deferred: putting it in the placeholder would rewrite 250 frame fixtures;
      the hint belongs in the footer segment, with its own golden pass.
- [ ] 3.2 The hint disappears while a turn runs, in bash mode and with a non-empty prompt.

## 3b. Layout

- [ ] 3b.1 The overlay must fit the live area's eight transient lines: the current block is trimmed
      from the top on a short terminal, so the scope title disappears before the rows. Decide
      between capping the window to the available lines and scrolling inside the block, then make
      the title survive.

## 4. Tests

- [ ] 4.1 Session tests: open/close, filter, scope, "? is literal with text", "stray Enter is ignored".
- [ ] 4.2 Footer tests: hint present when idle, absent when running.
- [ ] 4.3 Catalogue: every binding has a non-empty description (guards the overlay's content).

## 5. Docs

- [ ] 5.1 `docs/guide.md`: one line under Keys — press `?` for the bindings that apply now.
