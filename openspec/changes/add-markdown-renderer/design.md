# Design: Markdown renderer and technical formatting (T-19)

## Placement and surface

New files in `src/Lunate.Tui/`: `Markdown/MarkdownRenderer.cs` (public), `Markdown/MarkdownBlock.cs` (internal block model), `Markdown/InlineScanner.cs` (internal), `Markdown/SyntaxHighlight.cs` (internal, C#/JSON/shell), `TechnicalText.cs` (public). Snapshot fixtures under `tests/Lunate.Tui.Tests/fixtures/markdown/`. `Lunate.Tui` takes `Spectre.Console` 0.57.2; `Lunate.Tui.Tests` takes `Spectre.Console.Testing` 0.57.2 — both approved by the maintainer before apply; nothing else moves.

`MarkdownRenderer.Render(string markdown) -> IRenderable` — pure text in, renderable out. No terminal, no `IAnsiConsole`, no live area; T-22 writes the renderable to scrollback, T-20 adds tool blocks around it. Public types: none mention Rx (their only could-be API, `Spectre.Console.Rendering.IRenderable`, is the point of the exercise and is a stable public Spectre surface).

## Parsing (deliberately small)

Line-based block parser, then a minimal inline scanner inside block text:

- Blocks: ATX headings 1-6; paragraphs (blank-line separated); bullet lists (`-`, `*`, `+`, two-space indent steps, one nesting level rendered with indent); ordered lists (`1.`-style, numbers normalized); blockquotes (`>`); fenced code (```lang, label shown; unknown language renders plain).
- Inline: `**bold**`, `*italic*`/`_italic_`, `` `code` ``. Precedence: code first (no markup inside), then bold, then italic; no nesting beyond that, no HTML, no links in v1 (guide's subset). Unclosed markers render literally.
- **Escaping rule (pinned by tests):** every user string goes through `Markup.Escape` before markup assembly; a line like `[dim]not markup[/]` must render literally. Fenced-code content is escaped the same way before highlighting markup is applied around it.

## Rendering decisions

- Heading: bold; level 1-2 get a subtle style (e.g. underlined for 1), rendered as its own paragraph.
- Inline code: `[invert]`-ish or grey background per Spectre style capabilities — fixed style, snapshot-recorded.
- Lists: `•` bullets (or `-` when glyphs unavailable later; v1 fixed `•`), ordered uses `n.`; quote: dim with a `│` prefix line.
- Fenced code: a dim Panel? No — keep scrollback-friendly: a block with a dim `lang` label line, code lines rendered with highlight styles, no border box (borders cost scrollback width; the guide's screen model wants copy-friendly output).
- Highlighting v1: C# keywords + strings + comments; JSON keys/strings/numbers; shell keywords/strings/`$vars`. Small keyword sets, deterministic scanners (no regex backtracking traps; test each with nasty inputs).

## Snapshots

`Spectre.Console.Testing` `TestConsole` (plain-text profile) renders each feature to a string; compared byte-for-byte against committed goldens in `tests/Lunate.Tui.Tests/fixtures/markdown/` (`heading.txt`, `bold-italic.txt`, `inline-code.txt`, `lists.txt`, `quote.txt`, `fence-csharp.txt`, `fence-json.txt`, `fence-shell.txt`, `mixed.txt`, `escaping.txt`). Regeneration via the existing `LUNATE_TUI_UPDATE_GOLDENS=1` convention (same helper as T-18, refactored for reuse). No color-ANSI goldens in v1 — style correctness is checked by targeted assertions (e.g. inline code is not plain text) where cheap.

## TechnicalText

Static helpers, invariant by construction (`InvariantCulture` on every `ToString`, tests run under de-AT too):

- `Bytes(long)` → `812 B`, `12.3 KB`, `1.5 MB` (no culture separators).
- `Tokens(long)` → `1,540` (invariant thousands separators) — footer format.
- `Duration(TimeSpan)` → `9.2 s`, `1 m 12 s`, `1 h 03 m`.
- `Percent(double, decimals = 1)` → `12.5%`.
Table tests per helper incl. boundary values; de-AT pass is the falsifier.

## Out of scope

Scrollback commit and streaming paragraph flow (T-22), tool blocks and diffs (T-20), footer assembly (T-21), terminal glyph fallbacks (T-33), links, tables, images, task lists (guide's subset excludes them).

## Deviations

(Filled during apply; empty at proposal time.)
