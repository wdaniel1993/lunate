## Why

ADR numbering is not checked by the documentation gate, so duplicate assignments and gaps can go unnoticed until an author chooses an already-used or misleading next number. The filename prefixes are the durable source for a check that needs no build or file-content parsing.

## What Changes

- Add a documentation-gate requirement for unique, dense primary ADR numbers, checked from filenames in the existing `verify-docs` job.
- Require actionable diagnostics that identify the colliding or gap-bracketing files and report the next free number.
- Specify duplicate, gap, and clean-set fixtures for the check.
- Put the author-facing numbering rule in `docs/guide.md`; no ADR README currently exists.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `repo-quality`: extend the documentation verification gate with ADR-number validation.

## Impact

The follow-up implementation (#339) will touch the docs-profile script and its fixtures, plus `docs/guide.md`. This proposal changes no source code, tests, ADR records, or user-facing runtime behavior and adds no dependency. The repository already has numbered addenda that intentionally reuse their parent ADR number; the design records how those are distinguished from primary ADRs. A separate existing collision between two primary `0027` files is surfaced for maintainer resolution before the implementation can make the docs profile green.
