# Design: edit tier 3, start_line and closest-region errors (T-14)

## Structure

- `src/Lunate.Coding/EditTool.cs`: the tier engine (per-tier match collection, start_line narrowing, tier 3, refusals, closest region), the optional `start_line` argument and the result string.
- `src/Lunate.Tui/ToolBlocks/DiffRenderer.cs`: `TierStyle` gains `"indent"` next to `"normalized"` (flagged fallback).
- `tests/Lunate.Coding.Tests/EditCorpusTests.cs`: request passthrough for `start_line` and `file_name`.
- `tests/fixtures/edit-corpus/`: new cases (below).
- Specs: `agent-files` (edit tool + corpus), `tui` (diff tier label).

## Matching engine (pinned)

- Tier order: `exact` → `normalized` → `indent`. Each tier collects the 1-based start line of every matching window (window size = `old_text` line count).
- Per tier: **zero** matches → next tier. **One** match → apply. **Several** matches → when `start_line` is given, narrow to matches with `|start - start_line| <= 3`: exactly one → apply; zero or several within (or no `start_line`) → the ambiguity error, listing the start line of **every** match of that tier, file unchanged. A tier with several matches never falls through — never a guess.
- After `indent` (last tier): zero matches → the not-found path.

## Tier 3 — `indent` (pinned)

- A window matches when every `old_text` line equals its file line with leading and trailing whitespace ignored (`Trim`, compared ordinally); a blank `old_text` line maps to a whitespace-only file line, and at least one `old_text` line must be non-blank.
- Apply: the offset is the leading-whitespace character difference between the first non-blank `old_text` line and its matched file line. Every non-blank `new_text` line is shifted by it — positive prepends that many spaces, negative removes up to that many leading whitespace characters — and blank `new_text` lines stay blank. File endings/BOM/trailing-newline handling is unchanged from tiers 1–2.
- Rationale: the earlier uniform-prefix pin undershot `docs/guide.md` ("leading whitespace per line ignored. `new_text` is re-indented by the offset between the first line of `old_text` and the matched first line"); the spec is aligned to the guide, which is untouched (review findings 1/5).

## Whitespace-significant refusal (pinned)

- Files by extension (OrdinalIgnoreCase): `.py`, `.yaml`, `.yml`, `.mk`; or file name `Makefile` (OrdinalIgnoreCase).
- Tiers 1–2 run normally (an exact/normalized match applies as always). When tiers 1–2 find nothing on such a file, tier 3 is refused with exactly:
  `could not find old_text in {path}; the indent tier is disabled for whitespace-significant files — re-read the file and edit with the exact text`
  (No closest-region hint on these files.)

## Closest-region error (pinned)

- Non-whitespace-significant files, no match in any tier: score every window (size = `old_text` line count) by the count of lines equal after `TrimEnd` (ordinal); best score ≥ 1 → error with exactly:
  `could not find old_text in {path}; closest region (lines {first}-{last}):` followed by a newline and the region's lines with any trailing `\r` stripped (LF-joined).
  Tie → earliest window. Best score 0, or the file shorter than the window → the existing plain message `could not find old_text in {path}` (the current corpus case stays valid).

## Tool surface (pinned)

- Arguments: `path`, `old_text`, `new_text`, optional `start_line` (integer ≥ 1; description: "Optional 1-based line where old_text starts; disambiguates when old_text matches several places").
- Result: `edited <path> lines <first>–<last> (match: exact|normalized|indent)` (en dash as today). `EditDetails` is unchanged (path, range, tier, diff); the TUI label and the eval tier instrumentation (T-35) flow from the tier string.

## Corpus (pinned)

Runner: `request.json` gains optional `start_line` (passed through) and optional `file_name` (default `input.txt`; used for the temp copy target and the `path` argument). New cases (minimum):
- `indent-applied` — C# block indented uniformly 4 spaces more; expected bytes show `new_text` re-indented.
- `indent-normalized-applied` — indented block that also differs in trailing whitespace (covers the combined case).
- `indent-crlf-applied` — CRLF file; indent-only difference (flat `old_text`); applies and stays CRLF.
- `indent-nested-applied` — nested file block (8/12/8 spaces) against a base `old_text`; the offset comes from the first non-blank line pair.
- `indent-file-trailing-applied` — the file block is indented and carries trailing spaces; the shifted `new_text` replaces the window cleanly.
- `indent-dedent-applied` — `old_text` more indented than the file block; the negative offset removes leading whitespace from `new_text`.
- `closest-region-crlf-error` — CRLF file; the closest-region hint carries no `\r`.
- `indent-refused-python`, `indent-refused-yaml`, `indent-refused-makefile` — indentation-only difference on `.py` / `.yaml` / `Makefile`; `expected-error.txt` pins the refusal text.
- `start-line-applies` — two exact matches, `start_line` near the second; the second is replaced.
- `start-line-still-ambiguous` — two matches both within 3 lines of `start_line`; the error lists both.
- `start-line-ignored-single-match` — one match far from `start_line`; it still applies.
- `closest-region-error` — multi-line `old_text` absent, a similar block present; the error shows the region; file unchanged.
- `exact-ambiguity-error` — two exact matches, no `start_line`; lists both start lines.

## Deviations

1. The matching engine (tier window finding, indent offset, ambiguity message, whitespace-significant
   check and closest-region hint) lives in `src/Lunate.Coding/EditMatcher.cs`, an internal static
   helper, instead of inside `EditTool.cs` as the Structure section lists. `EditTool.cs` orchestrates
   the tiers and applies the edit; the extraction keeps both files under the repo's ~300-line rule.
   Behaviour is exactly as pinned.
2. A present-but-invalid `start_line` (not an integer, below 1, fractional, `null`) is refused with
   `start_line must be an integer greater than or equal to 1`; the design pins only the valid shape
   (optional integer ≥ 1) and says nothing about malformed values. Tool error texts are model-facing,
   so the tool names the problem and the next step instead of silently ignoring the argument.
3. Tier 3 was pinned as a uniform-prefix rule. That undershot `docs/guide.md` (leading whitespace per
   line ignored; re-indent by the first-line offset) and made tier 3 dead on CRLF files and files with
   trailing whitespace. The pin is amended above to the guide's per-line rule; the guide is untouched
   (review findings 1/5).
4. The closest-region pin joined the raw file lines, leaking a trailing `\r` on CRLF files. The pin is
   amended above: each region line's trailing `\r` is stripped (review finding 3).

## Seams

- Eval instrumentation ("how often tiers 2 and 3 are used") is T-35's concern.
- `@path`-style convenience hints beyond the guide are out of scope; the corpus is the truth.
