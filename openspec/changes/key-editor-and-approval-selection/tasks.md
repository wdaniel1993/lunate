# Tasks

One PR, four commits: the editor, the approval selection, the drift check, and the gate. Each commit is green on its own; the guide's table is edited in commit 1 and 2 (new rows) and only *checked* in commit 3, so the check never lands red.

## 1. `Ctrl+G` opens the draft in an editor

- [ ] 1.1 Red: routing `Ctrl+G` in every prompt context returns `RoutedKey.Editor`, and `Alt+G`, `Ctrl+Shift+G` and a bare `g` do not; the catalog still has no overlap and every row still has a description.
- [ ] 1.2 `RoutedKey.Editor` and the catalog row (`KeyKind.Character`, `Ctrl: true`, `Alt: false`, `Text: "g"`, `KeyContext.None`, "Open the draft in your editor"), placed beside the stash and history-search rows.
- [ ] 1.3 Red: `SuspendTerminal` on the fake console records quiesce → cooked → child → raw → resume in that order, and the session reads no key while the scope is held.
- [ ] 1.4 `IConsoleIO.SuspendTerminal()`; `SystemConsoleIO` quiesces the pump without completing the key channel, restores the process's original termios/console mode, and on dispose clears the decoder's partial sequence, re-enters raw mode and resumes the pump; `FakeConsoleIO` scripts the same transitions and exposes them. `PublicAPI.Unshipped.txt` entries for the new member.
- [ ] 1.5 Red: `$VISUAL` wins over `$EDITOR`, a blank value falls through, neither falls back to `vi` on POSIX and `notepad` on Windows, and a value with arguments is split into program and arguments with no shell involved.
- [ ] 1.6 The editor resolver: one pass over `VISUAL`, `EDITOR`, platform default; whitespace split; no quoting.
- [ ] 1.7 Red: the temp file is UTF-8 without a BOM, owner-only on POSIX, and gone after a zero exit, a non-zero exit, a launch that throws and a read-back that throws.
- [ ] 1.8 Red: a zero exit reloads the file with CRLF normalised and exactly one trailing newline removed, places the cursor at the end, and repaints the box at its new height; an empty draft opens an empty file, and an empty file leaves the draft empty.
- [ ] 1.9 Red: a non-zero exit, a missing program, a throwing start and a failing read-back each post exactly one notice naming the variable and value, the exit code, or the read that failed, and leave the draft, the cursor and the stash untouched; a set-but-unlaunchable `VISUAL` is *not* replaced by `EDITOR`.
- [ ] 1.10 Red: a draft changed while the child ran keeps the prompt, the editor's text lands in the stash slot, and one notice says so.
- [ ] 1.11 The session dispatch: `RoutedKey.Editor` takes the suspend scope and the live-area suspend, snapshots the draft and cursor, writes, runs the child with inherited handles and no redirection, reads back, applies D5, resumes both scopes in a `finally`.
- [ ] 1.12 Tests use a scripted child (an inline script or a test executable), never a real editor, and cover each row of D4's table plus 1.1–1.10.
- [ ] 1.13 `docs/guide.md`: a `Ctrl+G` row (what it does, that the program must wait — `code --wait`), and the key-table section's prose says where the draft goes on failure.

## 2. The approval selection

- [ ] 2.1 Red: `ApprovalPromptModel` starts on `[y]es`; Up from it lands on `[a]lways this session`, Down from the last lands on `[y]es`; Left, Right, Tab, PageDown and `k` leave both the selection and the decision untouched.
- [ ] 2.2 Red: `y`, `n` and `a` decide wherever the selection sits; Space activates the selected choice; Enter and Esc still deny; `Decide` still returns `null` for every unhandled key.
- [ ] 2.3 `ApprovalPromptModel` gains the selection index, the move (wrapping) and the activation; the model stays a record so a repost is a value, not a mutation.
- [ ] 2.4 Red: the panel renders `> ` before the selected choice's bracket, the inline row is 36 cells with all three choices visible, and the row is identical under `NO_COLOR` and the ASCII fallback.
- [ ] 2.5 Red: below 36 columns the choices stack one per row with a stable gutter; below 22 cells a label clips only after its key letter; no rendered line exceeds the width at 40, 60, 80 and 120.
- [ ] 2.6 `ApprovalPanelLayout` gains the marker, the stacked form and the letter-preserving clip; the existing 34-cell inline form and its spacing stay unchanged otherwise.
- [ ] 2.7 Red: at 40×12 with a long detail, every choice row and the question survive while the detail trims to nothing; the trim order never removes a choice row.
- [ ] 2.8 The session's approval branch reposts the prompt when the model moved and keeps its early return for every other key; `Ctrl+C`'s quit path is unchanged.
- [ ] 2.9 Golden frames: regenerate the approval panel at 40 and 80 columns, deliberately, in this commit.
- [ ] 2.10 `docs/guide.md`: the approvals paragraph gains Up/Down, Space, the wrap and the marker; the table gains the `y` / `n` / `a` row.
- [ ] 2.11 `PublicAPI.Unshipped.txt` for the model's new members.

## 3. The guide-table drift check

- [ ] 3.1 `docs/guide.md`: fold the six undocumented catalog keys into their cells — `Ctrl+J` / `Shift+Enter` / `Ctrl+Enter` into the newline row, `Ctrl+Delete` into the kill-word row, `Ctrl+Left` / `Ctrl+Right` into the word-motion row — and add the `Ctrl+P` (unbound) row.
- [ ] 3.2 Red: `GuideKeyTableTests` locates `docs/guide.md` by walking up to `lunate.sln` and fails with the path it looked for when the file is missing (never skips).
- [ ] 3.3 Red: a catalog key with no cell fails naming the key, its description and the direction; a cell key with no catalog row fails naming the key, the direction and the row's action text.
- [ ] 3.4 Red: a merged cell (`Ctrl+A` / `Ctrl+E`, four newline keys) passes; a key present only in an Action cell does not count, proven by removing `Ctrl+J` from the key cell while leaving the prose.
- [ ] 3.5 Red: a missing heading, missing table or missing `| Key | Action |` header fails naming what is missing.
- [ ] 3.6 The parser: heading-anchored, Key column only, cells split on `/`, backticks and whitespace trimmed, compared on `KeyPatternFormatter`'s spelling; the exemption table is `(key, reason)` rows for `y`, `n`, `a`, `?`, `Ctrl+P`.
- [ ] 3.7 Assert the check is not vacuous: the doc's key count is at least the catalog's row count, and every exemption still appears in the table (a stale exemption is itself a failure).

## 4. Gate

- [ ] 4.1 `openspec validate key-editor-and-approval-selection --strict` green, and `openspec validate --all --strict` still green.
- [ ] 4.2 `dotnet csharpier check .` clean; `tests/Lunate.Tui.Tests` and `tests/Lunate.Coding.Tests` green.
- [ ] 4.3 `scripts/verify.sh` green on both culture passes; the public-API gate green on the new `IConsoleIO` and `ApprovalPromptModel` members.
- [ ] 4.4 Manual smoke on a real terminal: `Ctrl+G` with `vim`, with a missing `VISUAL`, and with `EDITOR="vim -u NONE"`; a resize while the approval panel is open keeps the selection.
