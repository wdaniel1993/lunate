# Tasks: Tool blocks and diff rendering (T-20)

## 1. Model and summaries

- [x] 1.1 `ToolBlockModel`, `ToolDiffInfo`, `ToolBlockStatus`; PublicAPI.Unshipped entries
- [x] 1.2 `ToolArgsSummary`: per-tool rules (read/write/edit path; bash command; cs_* symbol; generic fallback); unit tests incl. adversarial inputs (missing fields, wrong types, invalid JSON, huge strings)

## 2. Output and status rendering

- [x] 2.1 `OutputExcerpt`: first 8 + last 8 with elision marker; boundary tests (≤17 lines whole, 18+ elided, single trailing newline trimmed, empty output)
- [x] 2.2 `ToolBlockRenderer`: header + status styles + excerpt; `Markup.Escape` on every user string; goldens `read-ok`, `bash-ok`, `error`, `long-output`, `running`, `no-output`, `escaping` + targeted ANSI assertions (green ok / red failed)

## 3. Diff panel

- [ ] 3.1 `DiffRenderer`: unified-diff parsing (file headers, hunks, +/-/context) + `match: <tier>` label (normalized yellow); unit tests for the parser (malformed input, CRLF, empty diff)
- [ ] 3.2 Goldens `edit-diff-exact`, `edit-diff-normalized`, `write-diff` + ANSI assertions (green +, red -, yellow normalized)

## 4. Close

- [ ] 4.1 csharpier; `bash scripts/verify.sh` green (incl. de-AT); `openspec validate add-tool-blocks --type change --strict`; self-review; commit per group; no push
- [ ] 4.2 Deviations recorded in design.md; note the T-22 mapping seam (`EditDetails` → `ToolDiffInfo`)
