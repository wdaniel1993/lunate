## Why

Card T-13: `edit` is the workhorse tool — the one that changes code — and its matching behavior is where silent wrong edits would happen, so it ships in tiers with uniqueness required at every tier (guide: "Fewer retries without silent wrong edits"). The guide fixes the tool's shape (parameters, result line naming the tier, diff in `Details`, errors) and the tier ladder (1 exact, 2 normalized — CRLF/LF alike, trailing whitespace ignored; 3 indentation-agnostic lands in T-14 with `start_line` and the closest-region hints). The corpus under `tests/fixtures/edit-corpus/` is the done-gate: one folder per case with `input`, `request.json` and `expected` or `expected-error.txt`.

## What Changes

- **`edit` tool** (`Lunate.Coding`, on the T-12 `Workspace`): `path`, `old_text`, `new_text`; tier 1 (exact) then tier 2 (normalized) matching, stopping at the first tier with exactly one match; ambiguity is an error listing all match line numbers, never a guess.
- **File state preservation**: the file keeps its line endings (CRLF stays CRLF), its BOM, and its trailing-newline state; `new_text` is re-terminated with the file's endings.
- **Result and details**: `edited <path> lines <first>–<last> (match: exact|normalized)`; `Details` carries an `EditDetails` with the line range, the tier and a unified diff (reusing the T-12 `LineDiff`).
- **Corpus**: `tests/fixtures/edit-corpus/` with the tier-1/2, state-preservation and error cases, driven by a folder-per-case runner test.
- **Spec**: `agent-files` gains the edit-tool and corpus requirements; its Purpose is updated to cover the third tool.
- Out of scope: tier 3 and whitespace-significant refusals, `start_line` disambiguation, closest-region hints (all T-14); eval tier counting (T-17); TUI diff rendering (T-19).

## Capabilities

### New Capabilities
- None.

### Modified Capabilities
- `agent-files`: adds the `edit` tool requirement (tiers 1–2, uniqueness, state preservation, result shape) and the edit-corpus requirement; Purpose updated.

## Impact

- New in `src/Lunate.Coding/`: `EditTool.cs`, `EditDetails.cs`; `TextFile` gains raw read/write helpers (BOM detection, ending style). New corpus fixtures + runner and direct tests in `tests/Lunate.Coding.Tests/`. No new packages; no CLI wiring.
