# ADR — Draw the box's floor, match file names without case, and wrap notices

Status: accepted (this change).

## Context

Three narrow-terminal defects reported from a real session: the input box loses its lower rule below 60 columns, `@` completion is case-sensitive, and a notice is clipped at the terminal edge — cutting exactly the part that says what to do.

## Decision

1. **The lower rule is drawn at every width.** A box without its floor is not a box; the compact band keeps its other reductions but not this one.
2. **File-name completion ignores case** while the index stays sorted and queried ordinally; only the completion match is case-insensitive.
3. **Notices wrap** to the terminal width and are capped at four lines with an ellipsis marker, so a pathological message cannot starve the input.

## Consequences

- Narrow sessions spend one more row on the box; the input keeps its eight-line budget and the tail loses a row.
- A mixed-case workspace completes for lowercase queries; ordering is unchanged.
- Every real notice fits in four lines today; if one needs more, the cap is a one-constant change.
- No packages, no config, no format change. Wider goldens stay byte-identical; only the narrow ones change.
