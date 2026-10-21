# TUI input polish

## Why

Three problems reported from a real narrow-terminal session (a phone SSH client), each small but each felt on every keystroke:

1. **The input box loses its lower boundary on narrow terminals.** Below 60 columns the lower rule is dropped, so the box reads as an open-topped strip and the footer looks attached to the input. A box without its floor does not look like a box.
2. **`@` completion is case-sensitive.** Typing `@te` finds nothing in a repo whose files start with uppercase letters (`TemplateExtension`, `TempDirectory`), even though the user's intent is unambiguous. The index's own ordering stays ordinal; only the match should ignore case.
3. **Notices are clipped, not wrapped.** A long notice — for example the clipboard helper being missing, with its install hint — is cut at the terminal edge, so the part that says what to do is exactly the part that disappears.

## What changes

- The input box draws its lower rule at **every** width. The compact band keeps its other reductions (short tool headers, compact footer, no autocomplete descriptions).
- The file index gains a **case-insensitive completion query**; the ordinal query stays for ordering. `@te` matches `TemplateExtension/`.
- Live-area notices **wrap** to the terminal width instead of clipping, each physical line sanitized and the whole notice bounded so a pathological message cannot starve the input box.

## Impact

- Change spec: `tui` (one modified requirement for the input box, one for the file index, one for autocomplete, plus the new notice-rendering requirement).
- Code: `LiveAreaRenderer`, `CellText`, `FileIndex`, `AutocompleteRenderer`.
- No new packages, no config, no migration. Sessions, transcript format and the public API are untouched.
