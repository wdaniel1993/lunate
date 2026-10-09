# Tasks: Markdown renderer and technical formatting (T-19)

## 1. Packages and TechnicalText

- [ ] 1.1 `Spectre.Console` 0.57.2 into `Lunate.Tui`; `Spectre.Console.Testing` 0.57.2 into `Lunate.Tui.Tests`; record approval in the change
- [ ] 1.2 `TechnicalText`: `Bytes`, `Tokens`, `Duration`, `Percent` — invariant by construction; table tests incl. boundaries; de-AT pass
- [ ] 1.3 PublicAPI.Unshipped entries for the public surface; golden regeneration helper shared with the frame goldens

## 2. Blocks

- [ ] 2.1 Block model + line parser: headings, paragraphs, bullets (indent), ordered, quotes, fences with language label; unit tests for the parser (no Spectre needed)
- [ ] 2.2 Renderables for each block + snapshot per feature: `heading.txt`, `lists.txt`, `quote.txt` (+ paragraph handling inside `mixed.txt`)

## 3. Inline and escaping

- [ ] 3.1 Inline scanner: code > bold > italic; unclosed markers literal; unit tests incl. nasty inputs
- [ ] 3.2 `Markup.Escape` on every user string; `escaping.txt` snapshot + targeted test that `[dim]…[/]` stays literal

## 4. Code fences and highlighting

- [ ] 4.1 Fenced code rendering with `lang` label; unknown language plain; snapshot `fence-csharp.txt`
- [ ] 4.2 Highlighting scanners: C# (keywords/strings/comments), JSON (keys/strings/numbers), shell (keywords/strings/`$vars`) — unit tests per scanner with adversarial inputs; snapshots `fence-json.txt`, `fence-shell.txt`
- [ ] 4.3 `mixed.txt` snapshot: one document containing every feature

## 5. Close

- [ ] 5.1 csharpier; `bash scripts/verify.sh` green (incl. de-AT); `openspec validate add-markdown-renderer --type change --strict`; self-review; commit per group; no push
- [ ] 5.2 Update the guide's test-stack note only if reality diverged (Verify stays unused; say so in deviations)
