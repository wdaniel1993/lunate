# Design — in-app key help

## Shape

```
KeyBindingCatalog.All ──▶ KeyHelpModel (rows, query, selection, scope)
                                  │
                                  ▼
                          KeyHelpRenderer ──▶ LiveArea block
```

`KeyHelpModel` is data only: the filtered rows, the selected index and the scope
(`Here`/`All`). The session owns opening, key dispatch and closing, exactly like the picker.

## Rows

A row is built from a catalog entry: `Describe(KeyPattern)` renders `Ctrl+R`, `Alt+Enter`,
`Shift+Tab`, `Esc`, `Space`, `Up` and a bare character as itself. The description is the
catalog's own text, so the overlay and the router cannot disagree.

Rows are bucketed by the context they are documented for — Editing, Turn running, Bash mode,
Approvals — with `KeyContext.None` rows listed under Editing. The `Here` view keeps the buckets
whose context is active; `All` keeps every bucket.

## Key handling

The overlay is consulted before the router, like the history search: while it is open, `j`, `k`,
arrows, `/`, `Backspace`, `Tab`, `Esc` and `?` belong to it and everything else is ignored
(help is read-only; a stray `Enter` must not submit a turn).

`?` opens only when the prompt is empty and no turn is running, matching the footer hint: a
user typing `?` mid-sentence means the character.

## Rendering

A single live-area block: title (`Keys — here` / `Keys — all`), the filter line when a query is
set, then the bucketed rows, windowed so the selection stays visible. The selected row is marked
`>` and emphasized. Widths come from the live area's column budget; the renderer clips to it
rather than letting a long description wrap the box.

## Failure modes

- An empty filter result renders `no binding matches` instead of an empty box.
- A binding whose context is not reachable (approval, trust, secret) is still listed under `All`;
  it is documentation, not an affordance.
