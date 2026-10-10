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

- A window matches when a single non-empty, whitespace-only prefix `P` exists such that every non-blank `old_text` line `p[i]` maps to a file line equal to `P + p[i].TrimEnd()`, and every blank `old_text` line maps to a whitespace-only file line. `P` is identical for all non-blank lines; at least one non-blank line is required.
- Apply: `new_text` replaces the window with each non-blank line prefixed by `P`; blank `new_text` lines stay blank. File endings/BOM/trailing-newline handling is unchanged from tiers 1–2.

## Whitespace-significant refusal (pinned)

- Files by extension (OrdinalIgnoreCase): `.py`, `.yaml`, `.yml`, `.mk`; or file name `Makefile` (OrdinalIgnoreCase).
- Tiers 1–2 run normally (an exact/normalized match applies as always). When tiers 1–2 find nothing on such a file, tier 3 is refused with exactly:
  `could not find old_text in {path}; the indent tier is disabled for whitespace-significant files — re-read the file and edit with the exact text`
  (No closest-region hint on these files.)

## Closest-region error (pinned)

- Non-whitespace-significant files, no match in any tier: score every window (size = `old_text` line count) by the count of lines equal after `TrimEnd` (ordinal); best score ≥ 1 → error with exactly:
  `could not find old_text in {path}; closest region (lines {first}-{last}):` followed by a newline and the region's lines verbatim (LF-joined).
  Tie → earliest window. Best score 0, or the file shorter than the window → the existing plain message `could not find old_text in {path}` (the current corpus case stays valid).

## Tool surface (pinned)

- Arguments: `path`, `old_text`, `new_text`, optional `start_line` (integer ≥ 1; description: "Optional 1-based line where old_text starts; disambiguates when old_text matches several places").
- Result: `edited <path> lines <first>–<last> (match: exact|normalized|indent)` (en dash as today). `EditDetails` is unchanged (path, range, tier, diff); the TUI label and the eval tier instrumentation (T-35) flow from the tier string.

## Corpus (pinned)

Runner: `request.json` gains optional `start_line` (passed through) and optional `file_name` (default `input.txt`; used for the temp copy target and the `path` argument). New cases (minimum):
- `indent-applied` — C# block indented uniformly 4 spaces more; expected bytes show `new_text` re-indented.
- `indent-normalized-applied` — indented block that also differs in trailing whitespace (covers the combined case).
- `indent-refused-python`, `indent-refused-yaml`, `indent-refused-makefile` — indentation-only difference on `.py` / `.yaml` / `Makefile`; `expected-error.txt` pins the refusal text.
- `start-line-applies` — two exact matches, `start_line` near the second; the second is replaced.
- `start-line-still-ambiguous` — two matches both within 3 lines of `start_line`; the error lists both.
- `start-line-ignored-single-match` — one match far from `start_line`; it still applies.
- `closest-region-error` — multi-line `old_text` absent, a similar block present; the error shows the region; file unchanged.
- `exact-ambiguity-error` — two exact matches, no `start_line`; lists both start lines.

## Deviations

1. The matching engine (tier window finding, indent prefix, ambiguity message, whitespace-significant
   check and closest-region hint) lives in `src/Lunate.Coding/EditMatcher.cs`, an internal static
   helper, instead of inside `EditTool.cs` as the Structure section lists. `EditTool.cs` orchestrates
   the tiers and applies the edit; the extraction keeps both files under the repo's ~300-line rule.
   Behaviour is exactly as pinned.
2. A present-but-invalid `start_line` (not an integer, below 1, fractional, `null`) is refused with
   `start_line must be an integer greater than or equal to 1`; the design pins only the valid shape
   (optional integer ≥ 1) and says nothing about malformed values. Tool error texts are model-facing,
   so the tool names the problem and the next step instead of silently ignoring the argument.

## Seams

- Eval instrumentation ("how often tiers 2 and 3 are used") is T-35's concern.
- `@path`-style convenience hints beyond the guide are out of scope; the corpus is the truth.
