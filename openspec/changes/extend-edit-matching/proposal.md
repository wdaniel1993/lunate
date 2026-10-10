# Proposal: edit tier 3, start_line and closest-region errors (T-14)

## Why

`add-edit-tool` shipped tiers 1–2 (`exact`, `normalized`) and explicitly deferred the rest of the guide's matching story: tier 3 (`indent`), `start_line` disambiguation, whitespace-significant refusals and closest-region errors. Without them the tool is stricter than the guide promises: a model that strips indentation gets a plain "not found", repeated blocks cannot be addressed by position, and Python/YAML edits fail without the re-read hint. T-14 finishes the matching story exactly as the guide's tool table and matching rules define it.

## What changes

- **Tier 3 (`indent`) in the edit tool (`Lunate.Coding`)**: a block whose non-blank lines carry the same non-empty whitespace prefix the `old_text` lines lack matches and applies with `new_text` re-indented by that prefix; the result names the `indent` tier.
- **`start_line` (optional argument)**: when a tier finds several matches, it narrows to the matches starting within 3 lines of `start_line`; exactly one applies, anything else is an error listing every match's start line — never a guess.
- **Whitespace-significant refusals**: `.py`, `.yaml`, `.yml`, `.mk` and `Makefile` refuse tier 3 with an error asking to re-read the file and edit with the exact text.
- **Closest-region errors**: a not-found error shows the closest region (the window with the most trimmed-equal lines) when anything resembles `old_text`.
- **Corpus growth**: the runner passes optional `start_line` and an optional `file_name` (default `input.txt`); new cases pin every tier-3, start_line, refusal and region behaviour byte-for-byte.
- **TUI**: the `indent` tier renders as a flagged fallback like `normalized` (one switch arm).

## Done when

The guide's matching table is fully covered by corpus cases (applied and error paths byte-exact); the edit tool requirement in `agent-files` and the diff-tier requirement in `tui` are updated; `scripts/verify.sh` green; no new packages.
