# Tasks: Markdown renderer and technical formatting (T-19)

## 1. Packages and TechnicalText

- [x] 1.1 `Spectre.Console` 0.57.2 + `Markdig` 1.4.0 (BSD-2-Clause) into `Lunate.Tui`; `Spectre.Console.Testing` 0.57.2 into `Lunate.Tui.Tests`; record approval + licence note in the change
- [x] 1.2 `TechnicalText`: `Bytes`, `Tokens`, `Duration`, `Percent` — invariant by construction; table tests incl. boundaries; de-AT pass
- [x] 1.3 PublicAPI.Unshipped entries for the public surface; golden regeneration helper shared with the frame goldens
- [x] 1.4 Startup/render cost: measure first parse+render (fresh process) and warm parse+render of the mixed document; report numbers, no gate (exe untouched by Tui)

## 2. Markdig parsing and blocks

- [x] 2.1 `MarkdownRenderer.Render(string) -> IRenderable` skeleton + Markdig pipeline (no extensions); AST mapper for headings, paragraphs, lists (incl. nesting), quotes, fences with language label; plain-text fallback for other block types
- [x] 2.2 Golden set (block level): `heading.txt`, `lists.txt`, `nested-lists.txt`, `quote.txt` (+ paragraph handling inside `mixed.txt`); `unclosed-fence.txt` (Markdig takes the rest as code)

## 3. Inline and escaping

- [x] 3.1 Inline mapping: literal, emphasis (bold/italic by delimiter), code, line breaks; links → `label (url)`, images → `alt (url)`, HTML → literal tag text (all plain, no markup)
- [x] 3.2 `Markup.Escape` on every user string; `escaping.txt` + targeted test that `[dim]…[/]` stays literal
- [x] 3.3 `emphasis-edges.txt` golden: `snake_case_words` stays literal (intraword `_`), `2*3*4` renders intraword emphasis per CommonMark (`2<em>3</em>4`)
- [x] 3.4 `unsupported.txt` golden + one falsifier per construct: table, link, image, HTML, task list render as readable plain text, never markup, never throw

## 4. Code fences and highlighting

- [x] 4.1 Fenced code rendering with `lang` label; unknown language plain; snapshot `fence-csharp.txt`
- [x] 4.2 Highlighting scanners: C# (keywords/strings/comments), JSON (keys/strings/numbers), shell (keywords/strings/`$vars`) — unit tests per scanner with adversarial inputs; snapshots `fence-json.txt`, `fence-shell.txt`
- [x] 4.3 `mixed.txt` snapshot: one document containing every feature

## 5. Close

- [x] 5.1 csharpier; `bash scripts/verify.sh` green (incl. de-AT); `openspec validate add-markdown-renderer --type change --strict`; self-review; commit per group; no push
- [x] 5.2 Deviations: parser replaced by Markdig per maintainer review (already noted in design.md); guide's test-stack note only if reality diverged (Verify stays unused)
