# Design — add-edit-tool (T-13)

## Where the code lives

`Lunate.Coding`: `EditTool.cs`, `EditDetails.cs`; `TextFile` gains raw helpers. Reuses `Workspace` (T-12) and `LineDiff` (T-12) for the `Details` diff. Tests: `tests/Lunate.Coding.Tests/` + the corpus under `tests/fixtures/edit-corpus/`.

## Matching engine (pinned)

- The file is read raw (UTF-8; the BOM is detected and remembered, not kept in the text). Lines are split on `\n`; each line keeps its exact content, including a trailing `\r`, for tier 1.
- `old_text` is split the same way. Its lines keep their `\r` too: `old_text` with CRLF endings matches a CRLF file at tier 1; `old_text` with LF endings falls through to tier 2.
- **Tier 1 (exact)**: a sliding window compares file lines to `old_text` lines verbatim (string equality of the raw lines).
- **Tier 2 (normalized)**: the same window compares `TrimEnd()` forms — trailing whitespace per line ignored, CRLF and LF treated alike.
- Every window position that matches is collected. The first tier with **exactly one** match applies; a tier with **several** matches is an error listing the start line number of every match ("make it longer so it matches once" — `start_line` disambiguation is T-14); a tier with none falls through; after tier 2 the tool reports not found (the closest-region hint is T-14).
- Uniqueness is required at every tier — the ladder stops at the first tier that has any match at all, so a tier-1 hit is never displaced by a later tier, and ambiguity never guesses.

## Application and state preservation (pinned)

- The matched file lines are replaced with `new_text`'s lines (split like `old_text`, trailing `\r` stripped — they are content, not endings; leading whitespace of `new_text` is preserved).
- All lines are rejoined with the **file's** dominant ending: the first `\r\n` or `\n` occurring in the file text (no newline in the file → `\n`). A CRLF file stays CRLF; an LF `new_text` never converts it.
- The file's trailing-newline state is preserved exactly: it ended with a newline → the result does too; it did not → nothing is added. `new_text`'s own trailing newline is ignored — the file's state governs.
- The BOM state is preserved; the encoding is UTF-8 in v1 (non-UTF-8 files are read with replacement and noted as a v1 limitation).
- Result: `edited <relative path> lines <first>–<last> (match: exact|normalized)` — en dash; the range is the new content's line range (`start … start + new_text lines − 1`).
- `Details`: `EditDetails(string Path, int FirstLine, int LastLine, string MatchTier, string Diff)` — `Diff` is `LineDiff.Unified(oldText, newText, path)` (UI-only, as with `write`).

## Errors (T-13)

- Outside the workspace, missing file, directory (from `Workspace`/tool checks, same texts as `read`/`write`).
- `old_text must not be empty`; `old_text and new_text are identical; nothing to change`.
- Not found: `could not find old_text in <path>`.
- Ambiguous: `old_text matches <n> places in <path> at lines <a>, <b>, …; make it longer so it matches once` (line numbers culture-invariant, ascending).
- Missing/non-string arguments → error results with instructive text.

## The corpus

- `tests/fixtures/edit-corpus/<case>/`: `input` (file), `request.json` (`old_text`, `new_text`), and `expected` (exact file bytes) or `expected-error.txt` (exact error output).
- Runner `EditCorpusTests` iterates every folder: copies `input` into a temp workspace, runs the tool on the copy, then asserts the file matches `expected` byte-for-byte or the result is an error whose output equals `expected-error.txt` exactly. Folders are self-contained so T-14 only adds cases.
- T-13 cases: `unique-exact`, `first-line-match`, `two-matches-error`, `normalized-ambiguity-error`, `crlf-file-lf-old-text`, `trailing-spaces-normalized`, `bom-kept`, `boundary-no-trailing-newline`, `empty-old-text-error`, `identical-old-new-error`, `not-found-error`.
- The 5 MB performance case (applied in under 200 ms) is a direct test in `EditToolTests` with a generated file, not a committed fixture.

## Deliberate non-goals

- Tier 3, whitespace-significant refusals, `start_line`, closest-region hints: T-14.
- No similarity/Levenshtein matching in v1 (guide).
- No CLI/registry wiring, no eval counters.
