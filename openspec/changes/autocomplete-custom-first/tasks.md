## 1. Ordering

- [x] 1.1 `CompletionRegistry.Compose` sorts custom kinds ahead of built-ins, stable inside each group.
- [x] 1.2 Registry test: custom rows precede built-ins in a mixed list.

## 2. Budget

- [x] 2.1 `AutocompleteState.Cap` returns the rows and the hidden count; the session publishes both.
- [x] 2.2 Renderer appends `… +N more — keep typing to filter` (ASCII fallback) and clips it to the width.
- [x] 2.3 Renderer tests at 40 and 120 columns; a list that fits has no indicator.

## 3. Documentation

- [x] 3.1 `docs/guide.md` states the ordering and the indicator.
