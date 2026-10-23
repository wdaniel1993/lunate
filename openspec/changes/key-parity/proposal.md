# Key parity with Claude Code

## Why

People arrive from Claude Code with its keyboard in their fingers. Ours diverges in ways that cost real time: `Ctrl+C` never cancels a turn (it clears or quits), `Ctrl+L` opens the model picker instead of redrawing, `Ctrl+P` cycles favourites that the picker already lists, and the standard line-editor keys (`Ctrl+A/E/K/U/W/Y`, `Alt+B/F/D`) do nothing. The mapping also lives in two places that already drift: a `switch` in `KeyRouter` and a hand-written table in `docs/guide.md`.

## What changes

- **One binding catalog** — `(key, intent, contexts, description)` rows become the single source for routing, the guide's table and the upcoming `?` help overlay. `KeyRouter` stops being a `switch` and routes from the catalog; a matrix test proves every row routes to exactly its intent.
- **Claude Code's chat bindings**: `Ctrl+C` interrupts a running turn, clears an idle prompt with text, and arms the quit window on an empty one; `Ctrl+D` quits on a second press within 800 ms; `Ctrl+L` forces a full redraw preserving input and conversation; `Alt+P` opens the model picker; `Ctrl+P` is unbound (favourites cycling is removed — the picker lists them).
- **New prompt capabilities**: `Ctrl+R` history search, `Ctrl+G` external editor (`$VISUAL`/`$EDITOR`), `Ctrl+S` stash/restore the draft, `Ctrl+_` undo.
- **The line editor gains the readline set**: `Ctrl+A`/`Ctrl+E`, `Ctrl+K`/`Ctrl+U`/`Ctrl+W`, `Ctrl+Y`, `Alt+B`/`Alt+F`, `Alt+D`, plus a real undo stack.
- **Approvals** keep Enter = deny and gain `Esc` (deny), `Up`/`Down` selection and `Space` to activate the highlighted choice; `y`/`n`/`a` still decide directly.

## Out of scope

- `Ctrl+V` image paste (platform clipboard work; its own change).
- `Ctrl+Enter` / `Ctrl+X Ctrl+S` send-now and other chords (multi-stroke sequences are a different mechanism).
- `Shift+Tab` permission-mode cycling (Lunate has no permission modes).
- A keybindings file and `/keybindings` remapping (P8, `t_7b529518`); this change makes the catalog that file will edit.
- The `?` overlay and footer hint (next change, `t_6f30eec2`).

## Impact

`openspec/specs/tui/spec.md` — MODIFIED: one meaning per key; Ctrl+C quit window; approval prompt; input line editing. No public API break: `RoutedKey` gains members, `KeyRouter.Route` keeps its shape.
