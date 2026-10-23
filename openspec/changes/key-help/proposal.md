# In-app key help

## Why

The product knows every binding — `KeyBindingCatalog` is the single place a key's meaning is written down — but the only way for a user to see them is `docs/guide.md`, which no one reads at the moment they are stuck. Terminal tools that feel discoverable (lazygit's `?`, k9s's help, helix's which-key) all answer "what can I press *here*?" without leaving the screen.

The old state was worse than undocumented: the guide's table and the router were two hand-kept lists that could silently disagree.

## What changes

- A dim `? keys` hint sits beside the mode hint in the status footer while the prompt is idle.
- `?` on an empty prompt opens a **read-only overlay** listing the bindings that apply in the current context: one row per binding, `key — action`, the same text the catalog carries.
- `j`/`k` (and the arrows) move the selection, `/` starts a filter, `Tab` switches between **here** (the active contexts) and **all** (every binding), `Esc` or `?` closes. The overlay never changes session state.
- `?` typed into a non-empty prompt stays a literal character: opening help must never eat text.

## Impact

- Spec: `tui` — one added requirement.
- Code: `KeyBindingCatalog` gains a display formatter; new `KeyHelpModel`/`KeyHelpRenderer`; `InteractiveSession` grows one overlay state and its key handling; `StatusFooterRenderer` gains the hint slot.
- No packages, no config, no persistence. Sessions, transcript and the public API are untouched.
