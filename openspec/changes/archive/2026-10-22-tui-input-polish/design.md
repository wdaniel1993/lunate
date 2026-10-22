# Design — TUI input polish

## 1. The lower rule

**Problem.** `LiveAreaRenderer.HasLowerRule` is `width >= WidthBands.NarrowWidth` (60). The comment claims the upper rule alone "keeps the box", which is not how a box reads: without a floor the input merges with the footer.

**Decision.** Draw the lower rule at every width. The renderer already reserves the line (`lowerRuleLines`) and the input budget already accounts for it, so this is one predicate.

**Consequence.** In the compact band the live area spends one more row on the box. The budget (`MaxLines = 8`) already covers this; only the tail has one row less, which is correct — the input is the surface the user is typing into.

**Alternatives considered.** *Draw a shortened lower rule* — no better than none, and the box edges still fail to line up. *Draw it only when the footer is present* — makes the box's shape depend on unrelated state. Rejected both.

## 2. Case-insensitive file completion

**Problem.** `FileIndex.Complete` filters with `StartsWith(prefix, StringComparison.Ordinal)`, so a query is case-sensitive in exactly the place users type partial names from memory.

**Decision.** Match with `StringComparison.OrdinalIgnoreCase` and keep the index sorted and the *ordering* ordinal, so results stay stable and a case-insensitive match never reorders the list.

**Consequence.** A mixed-case workspace completes for lowercase queries. The ordinal query remains available for callers that need exact prefix semantics; only completion changes.

## 3. Wrapped notices

**Problem.** `LiveAreaRenderer.Render` runs a notice through `CellText.SingleLine` and then `CellText.Clip`, so the tail of a long notice is discarded. The clipboard-helper message is the clearest example: the install hint is last.

**Decision.** Wrap a notice to the terminal width with the existing word-wrap helper, sanitize each physical line, and cap the notice at a small number of lines (`MaxNoticeLines = 4`) with an ellipsis marker on the last if it overflows. The pre-input line count accounts for the actual number of notice lines, so the input box keeps its eight-line budget and the tail is reduced instead.

**Why cap.** A pathological notice (a stack trace pasted into a message) must not consume the whole live area and hide the input. Four lines covers every real notice in the product today with room to spare.

**Alternatives considered.** *Let the notice scroll in the scrollback instead* — notices are transient status, not transcript; moving them changes the product's meaning. *A single line with a "…more" marker and `ctrl+o`* — adds a key binding for a rare case. Rejected.

## Tests

Each change is pinned by a renderer or index test at the widths that matter: 40/59/60/80 for the rule, mixed-case fixtures for completion, and a long notice at 40 and 80 columns. The existing goldens are regenerated only where the rule changes (40 columns and any narrow fixture); every wider golden must stay byte-identical.
