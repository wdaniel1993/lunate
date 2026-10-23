# Design: one catalog, one route per key

## Shape

```csharp
public readonly record struct KeyBinding(
    KeyPattern Key,        // kind + modifiers + text; the same shape KeyRouter matches today
    RoutedKey Intent,
    KeyContext Contexts,   // [Flags]: Idle, Running, Queue, Approval, Picker, Transcript, Bash
    string Description);   // shown by the ? overlay, searched by its filter, checked by a test
```

`KeyBindingCatalog.All` is an ordered, immutable list. `KeyRouter.Route(key, state)` walks it and returns the first row whose `Key` matches and whose `Contexts` contain the active state; no row means `RoutedKey.Edit`.

Order is the conflict policy: a row never overlaps another row's context with the same pattern — a test asserts that pairwise, so "one meaning per key" is a property of the data rather than a reviewer's attention.

## Why data and not the switch

Three consumers need the same truth: routing, the guide's table, and the `?` overlay that follows. The switch keeps the truth in code that cannot describe itself, so every documentation change is a second edit that can silently disagree. The last three rekeys all touched the switch, the guide table and the tests separately.

The catalog is also the seam a keybindings file (P8) and extension bindings will edit: add or override rows, and routing plus display follow. Neither is in this change, but the shape is chosen so they can land without a second refactor.

## Contexts

Routing today takes `turnRunning` and `hasQueuedFollowUps`. The catalog generalises that to context flags so later surfaces (approval, picker, transcript, bash mode) describe themselves instead of threading more booleans into `Route`. The approval and picker keys are not routed through the prompt router — their panels own their keys — but their rows live in the catalog so the help overlay can list them.

## Editor keys are intents, not edits

`Ctrl+K` and `Ctrl+W` change the buffer, not the session. Two options: leave them inside the input line's own key handling (invisible to the catalog, so the overlay cannot list them), or give them intents and let the session dispatch them to the input line. This change takes the second: `RoutedKey` gains `KillToEnd`, `KillToStart`, `KillWordBack`, `KillWordForward`, `Yank`, `Undo`, `WordPrevious`, `WordNext`, `LineStart`, `LineEnd`, so every catalog row has exactly one intent and the matrix test covers all of them.

## Undo

`Ctrl+_` needs an undo history the input line does not keep. The design: each mutation pushes the previous `InputLineState` onto a bounded stack (64 entries), a newline or submit clears it, and undo pops. No coalescing of consecutive typing in this change — one key, one undo step — because coalescing needs a timer and the product's rule is virtual time only.

## Ctrl+C and Ctrl+D

Both use the existing `CtrlCQuitWindow` shape (injected scheduler, no wall clock). `Ctrl+C` becomes three-way: running → interrupt (cancel the turn, keep the queue), idle with text → clear, idle and empty → arm/quit within two seconds. `Ctrl+D` quits on a second press within 800 ms, idle and empty only: quitting mid-turn destroys work with no recovery path, and Claude Code's own exit confirmation exists to prevent exactly that accident. The asymmetry is documented in the spec and the guide.

## History search

`Ctrl+R` opens an inline filter over the prompt history (most recent first), keys: typing narrows, `Ctrl+R`/`Down` moves to the next match, `Enter` places it in the input, `Esc` cancels. It reuses the autocomplete's list rendering rather than a new widget.

## External editor

`Ctrl+G` writes the draft to a temp file, suspends the TUI, runs `$VISUAL` or `$EDITOR` (falling back to `vi`), restores the screen and reads the file back. Failure to start the editor leaves the draft untouched and posts a notice. Tests use a scripted child process, never a real editor.

## Stash

`Ctrl+S` stashes the current draft into a single slot and clears the prompt; pressing it on an empty prompt restores the stash. The slot is session-local and not persisted; a second stash while one exists replaces it, with a notice naming what was replaced.

## Docs

`docs/guide.md`'s key table stops being prose input: a test parses its rows and asserts every catalog row appears verbatim, so a routing change without its table row fails the build.
