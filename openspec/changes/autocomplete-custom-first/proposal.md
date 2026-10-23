## Why

A bare `/` shows eight rows and the built-ins fill all eight, so `/skill:<name>` rows and extension commands sit below the fold: the user concludes custom commands are missing. The list also drops the tail without a word, so nothing tells the user that more exists.

## What Changes

- Custom completion entries (skills, extension commands) sort ahead of the built-ins; the order inside each group is unchanged.
- When the filtered list does not fit the eight-row budget, the last row becomes `… +N more — keep typing to filter` and one fewer item is shown.
- The guide states both rules.

## Impact

- `src/Lunate.Coding/CompletionRegistry.cs` (ordering), `src/Lunate.Coding/InteractiveSession.Autocomplete.cs` (cap), `src/Lunate.Tui/Autocomplete.cs` (`HiddenCount`, `Cap`), `src/Lunate.Tui/AutocompleteRenderer.cs` (indicator row).
- `openspec/specs/tui/spec.md` gains one requirement.
