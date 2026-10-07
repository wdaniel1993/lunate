# Tasks — add-edit-tool (T-13)

## 1. Matching and application engine (TDD)

- [x] 1.1 `TextFile` raw helpers: read text with BOM detection, dominant line-ending detection, write preserving BOM and ending state
- [x] 1.2 Tier 1 (verbatim) and tier 2 (trimmed; CRLF/LF alike) sliding-window matching with all-match collection; stop at the first tier with exactly one match; several matches → ambiguity error listing every start line; none after tier 2 → not-found error
- [x] 1.3 Application: replace matched lines with `new_text` lines, rejoin with the file's ending, preserve BOM and trailing-newline state; result `edited <path> lines <first>–<last> (match: exact|normalized)`; `EditDetails` with range, tier and `LineDiff.Unified` diff
- [x] 1.4 Argument validation and error results (empty `old_text`, identical texts, missing/non-string args, outside workspace, missing file, directory)

## 2. Corpus

- [x] 2.1 Folder-per-case scaffolding + `EditCorpusTests` runner (copy `input` to temp, run, assert `expected` bytes or exact `expected-error.txt`)
- [x] 2.2 Cases: `unique-exact`, `first-line-match`, `two-matches-error`, `normalized-ambiguity-error`, `crlf-file-lf-old-text`, `trailing-spaces-normalized`, `bom-kept`, `boundary-no-trailing-newline`, `empty-old-text-error`, `identical-old-new-error`, `not-found-error`

## 3. Direct tests

- [x] 3.1 State preservation: CRLF stays CRLF, BOM kept, no trailing newline added, leading whitespace of `new_text` preserved
- [x] 3.2 5 MB file applies in under 200 ms locally (generated file, not a fixture); the test enforces a 1.5 s CI tripwire on shared runners (see design.md)

## 4. Close

- [x] 4.1 `agent-files` spec Purpose updated to cover the third tool (direct edit; delta Purpose is ignored for existing capabilities)
- [x] 4.2 `dotnet csharpier format .`, `bash scripts/verify.sh` green (incl. de-AT), `openspec validate add-edit-tool --type change --strict`, self-review
