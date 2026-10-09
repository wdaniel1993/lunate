# Design: Markdown renderer and technical formatting (T-19)

## Packages, licence, startup

`Lunate.Tui` adds `Spectre.Console` 0.57.2 and `Markdig` 1.4.0; `Lunate.Tui.Tests` adds `Spectre.Console.Testing` 0.57.2. **Markdig 1.4.0 is BSD-2-Clause** (permissive; recorded in the licence notes — not MIT like the rest) and has no dependencies on net10.0. Startup: the exe (`lunate`) never touches Tui types, so the 150 ms budget is untouched; Markdig loads lazily on first markdown render. The apply measures and reports (a) first parse+render of the mixed document in a fresh process, (b) warm parse+render — informational, no gate yet; T-22/T-32 and ADR-0009 own real budgets once the TUI sits in the exe's path.

## Structure

New files in `src/Lunate.Tui/`: `Markdown/MarkdownRenderer.cs` (public), `Markdown/MarkdownAstMapper.cs` (internal — Markdig AST → Spectre), `Markdown/SyntaxHighlight.cs` (internal, own scanners), `TechnicalText.cs` (public). Goldens under `tests/Lunate.Tui.Tests/fixtures/markdown/`.

`MarkdownRenderer.Render(string markdown) -> IRenderable` — pure text in, renderable out. No terminal, no `IAnsiConsole`, no live area; T-22 writes the renderable to scrollback, T-20 adds tool blocks around it. Public types mention no Rx.

## Parsing: Markdig

`Markdig.Markdown.Parse(text)` with a deliberately plain pipeline (no extension features; the subset decides what rendering exists, not the parser). The mapper walks `MarkdownDocument`:

- `HeadingBlock` → bold, level 1 slightly stronger (underline); own paragraph.
- `ParagraphBlock` → inline walk (below), escaped.
- `ListBlock` (bullet/ordered) → `•` / `n.` with two-space indent per level; nested lists recurse with growing indent (golden pins this).
- `QuoteBlock` → dim, `│`-prefixed lines.
- `FencedCodeBlock` / `CodeBlock` → borderless block: dim `lang` label line (unknown language renders plain), code through our highlight scanners. **Unclosed fence**: Markdig takes the rest as code; golden pins it.
- Every other block type → plain-text path (never markup, never throw).

Inline mapping (all escaped before assembly): `LiteralInline` → text; `EmphasisInline` → bold/italic by delimiter; `CodeInline` → code style (no markup inside); `LineBreakInline` → break/space; links → `label (url)` plain; images → `alt (url)` plain; HTML inline → literal tag text; unrecognized → literal text. Emphasis edge cases (`snake_case_words` must not italicise; `2*3*4` stays literal) are Markdig's intraword rules now — goldens confirm end-to-end.

**Escaping rule (pinned by tests):** every user string goes through `Markup.Escape` before Spectre assembly; `[dim]not markup[/]` renders literally. Highlight markup is assembled around already-escaped content.

## Rendering decisions

- Heading bold (level 1 underlined); inline code styled distinctly (fixed style, snapshot-recorded); lists `•`/`n.`; quote dim with `│`.
- Fenced code **borderless** — a dim language label line, code lines with highlight styles, no box (scrollback copy-friendly).
- Highlighting v1: C# keywords + strings + comments; JSON keys/strings/numbers; shell keywords/strings/`$vars` — small keyword sets, deterministic scanners, adversarial-input tests (no regex backtracking traps).

## Snapshots

`TestConsole` (plain-text profile) renders each fixture; byte-compared against goldens in `tests/Lunate.Tui.Tests/fixtures/markdown/`: `heading.txt`, `bold-italic.txt`, `inline-code.txt`, `lists.txt`, `nested-lists.txt`, `quote.txt`, `fence-csharp.txt`, `fence-json.txt`, `fence-shell.txt`, `unclosed-fence.txt`, `emphasis-edges.txt`, `escaping.txt`, `unsupported.txt`, `mixed.txt` (all features in one document). Regeneration via the existing `LUNATE_TUI_UPDATE_GOLDENS=1` convention. No color-ANSI goldens in v1 — style correctness via targeted assertions where cheap.

## TechnicalText (unchanged)

Static helpers, invariant by construction: `Bytes` (`812 B`, `12.3 KB`), `Tokens` (`1,540`), `Duration` (`9.2 s`, `1 m 12 s`), `Percent` (`12.5%`). Table tests per helper incl. boundaries; de-AT pass is the falsifier.

## Out of scope

Scrollback commit and streaming flow (T-22), tool blocks and diffs (T-20), footer assembly (T-21), terminal glyph fallbacks (T-33), rendered links/tables/images (plain text only), TextMate-grade highlighting.

## Deviations

- **Parser replaced by Markdig per maintainer review (2026-10-09).** The originally proposed hand-written block parser and `InlineScanner` were dropped before implementation; rationale: CommonMark inline correctness on real agent output (`snake_case`, globs, nested emphasis) is the risky part, not the block level. The renderer (AST → Spectre, escaping, highlighting, snapshots) stays in-house. Recorded per maintainer instruction.
- **Markdig licence is BSD-2-Clause**, not MIT — checked during the same review and accepted.
