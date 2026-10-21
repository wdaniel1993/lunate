# Tasks — TUI input polish

## 1. Lower rule at every width

- [x] 1.1 Add red tests: at 40, 59, 60 and 80 columns the rendered frame has a full-width lower rule directly below the input when the input is one line, and below the last input line when it wraps.
- [x] 1.2 Make `LiveAreaRenderer.HasLowerRule` always true; keep the budget arithmetic exact so the input still gets its eight lines and the tail loses only what the rule needs.
- [x] 1.3 Regenerate only the narrow fixtures (40 columns and any compact-band golden); prove every wider golden is byte-identical.
- [x] 1.4 Commit: `fix(tui): draw the input box's lower rule at every width`.

## 2. Case-insensitive file completion

- [x] 2.1 Add red tests: a mixed-case fixture (`TemplateExtension/`, `TempDirectory.cs`) completes from `te`, `TE`, `Te`; ordering stays ordinal; the ordinal query keeps its exact-prefix semantics.
- [x] 2.2 Match completion with `StringComparison.OrdinalIgnoreCase` while keeping the index sorted ordinally.
- [x] 2.3 Commit: `fix(coding): complete file names without regard to case`.

## 3. Wrapped notices

- [ ] 3.1 Add red tests: a notice longer than the width wraps at 40 and 80 columns, no word is lost, every physical line is at most the width, and a notice that overflows `MaxNoticeLines` ends with the ellipsis marker.
- [ ] 3.2 Add `MaxNoticeLines = 4`; wrap the notice in the renderer, sanitize each physical line, and feed the real count into the pre-input budget.
- [ ] 3.3 Commit: `fix(tui): wrap a long notice instead of clipping it`.

## 4. Verification

- [ ] 4.1 `dotnet build`, `dotnet csharpier check .`, the Tui and Coding suites, and `openspec validate tui-input-polish --strict`.
- [ ] 4.2 Update `docs/guide.md` where it states the lower rule's 60-column boundary.
