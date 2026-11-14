# Change the theme and remap a key

This page answers how to change the terminal's colours and rebind an input key, and the short answer
is `/settings`: `tui.theme` chooses the colour-depth policy and `tui.keys <binding id> <key>` rebinds
one key, both persisted so they survive a restart.

## Choose a theme

`/settings tui.theme` chooses the built-in theme for the session: `default` keeps the terminal's
detection, `ansi16` pins the high-contrast sixteen-colour system, and `mono` drops every attribute.
The choice persists in the settings file's `tui` section, and a [theme file](themes.md) can recolor
the tokens of whichever built-in is selected. A theme can only lower the depth, so `NO_COLOR` still
wins.

## Remap a key

`/settings tui.keys <binding id> <key>` remaps a binding: the catalogue row keeps its id, intent,
context and description and gains the one key — `keys.json` stays the only place a key is declared.
An unknown id is refused with the closest ids named, a key an earlier row already answers is refused
with that row named, and any remap persists so it survives a restart.

The binding ids come from the [key binding table](keybindings.md).

## Related

- [Themes](themes.md) — the built-in themes, the token file and the colour downgrade
- [Settings reference](settings.md) — the `tui.theme` and `tui.keys` rows
- [Usage](usage.md) — the keys the prompt and the transcript use
- [Run tool calls in a container](execution-environments.md) — the same session inside one container
