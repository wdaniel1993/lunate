# Themes

This page answers how to change the terminal's colours: the three built-in themes, the token file
that replaces individual colours, and what happens on a terminal that cannot show the colour you
picked.

Every colour in the interface is a *semantic token*, never a widget's own value, so a token file
survives a new surface and a new surface inherits every theme.

## Pick a theme

The theme is chosen once at startup, and `/settings tui.theme` changes it for the rest of the
session. The order is the flag, then the setting, then the default:

```bash
lunate --theme ansi16
```

| Theme | What it paints |
| --- | --- |
| `default` | The palette Lunate ships: hex values on a true-colour or 256-colour terminal, their ANSI fallbacks on the sixteen-colour set |
| `ansi16` | Named ANSI colours only, for a sixteen-colour terminal |
| `mono` | No colour at all; emphasis stays in the weight and the words |

`--theme` also accepts the path of a [token file](#a-theme-file): that file is read for this run
instead of the one in the Lunate home. `--theme <name>` still reads the home file and overlays it on
the named built-in.

## Tokens

| Token | What paints with it |
| --- | --- |
| `accent` | The product's own voice: the welcome mark, a tool's name, the selected autocomplete row, an approval's key letters |
| `muted` | Secondary text: comments, a tool's detail block, the status footer |
| `error` | A failed tool call and an error line |
| `success` | A completed tool call and a string literal in highlighted code |
| `warning` | An approval headline, an elided tool excerpt, a rematch tier label, a number in highlighted code |
| `code` | Inline code, a fenced block without a grammar, a JSON key |
| `diff.add` | An inserted diff line |
| `diff.remove` | A deleted diff line |
| `prompt` | The prompt marker and the committed user echo |
| `notice` | A notice and the thinking block: information the session volunteers |

Themes never change glyphs or layout. The ASCII stand-ins and the box characters remain the
capability system's job, and the bold and dim attributes stay whichever token is in use.

## A theme file

`theme.json` in the Lunate home (`~/.lunate/theme.json`; `$LUNATE_HOME` moves the home wholesale)
maps any of the ten tokens to a colour. The file is read once at startup and overlays the selected
built-in: a token it does not name keeps the built-in's colour, so a file that changes only the
accent leaves everything else as it was.

A colour is one of three forms:

- an ANSI name — `red`, `green`, `bright-cyan`, `magenta` — painted as that name at every depth
- a 256-colour index — `"colour: 208"` — painted as that entry wherever the terminal has 256 colours
- a hex value — `"#ff8800"` — painted exactly on a true-colour terminal and downgraded elsewhere

[The example file](examples/theme.json) sets all ten tokens and uses all three forms.

An unknown token fails the load with the ten valid names listed, and a colour that cannot be read
names the token and the accepted forms. A file that fails anywhere is discarded whole — the selected
built-in keeps every token and the session starts with one line saying why — so a half-applied theme
is never in effect.

## Colour depth and `NO_COLOR`

A theme can only lower the colour depth, never raise it back. A hex value on a 256-colour terminal
becomes the nearest xterm entry, on a sixteen-colour terminal the nearest of the sixteen, and on a
colourless terminal no colour at all. `NO_COLOR` and the detection rules win over every theme, so a
theme file cannot turn colour back on in a session that has none.

## Related

- [Change the theme and remap a key](themes-and-keys.md) — the `/settings` rows behind these choices
- [Terminal setup](terminal-setup.md) — the colour and Unicode capabilities a terminal reports
- [Settings reference](settings.md) — the `tui.theme` row
- [Environment variables](environment-variables.md) — `NO_COLOR`, `COLORTERM` and `LUNATE_HOME`
