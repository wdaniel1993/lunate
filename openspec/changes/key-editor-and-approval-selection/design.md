# Design: the editor key, the approval selection, and a table that cannot drift

## Context

`KeyBindingCatalog` is an ordered list of `(pattern, intent, contexts, description)` rows; `KeyRouter.Route` returns the first row that matches and applies, and the guide table, `?` overlay and router all read it. `RoutedKey` has no editor intent and the catalog has no `Ctrl+G` row, although the spec names it and `key-parity`'s design described the flow. The approval panel is `ApprovalPromptModel.Decide(KeyEvent)`: a pure `y`/`n`/`a`/Enter/Esc mapping that returns a choice or `null`, with `ApprovalPanelLayout` rendering a fixed choice string. Nothing in it can move or show a selection, and no state survives a repaint because there is no state to survive. `docs/guide.md`'s table is written by hand: 25 keys documented, 30 in the catalog, and one documented key (`?`) that deliberately has no row.

This change closes those three gaps without touching the harness, the session format or the public CLI.

## Goals and non-goals

**Goals.** `Ctrl+G` behaves the way the spec already says, on every platform, with a failure that never costs the draft. The approval selection is real, visible, and survives a repaint. The guide table cannot disagree with the catalog without a red test.

**Non-goals.** A keybindings file or `/keybindings` (P8, `t_7b529518`); extension-contributed bindings; `j`/`k` on approvals; a fourth approval choice; any tape or recording (item 4 of `t_7a383140` belongs to the video-regeneration card).

## Decisions

### D1 — `Ctrl+G` is one catalog row, and the catalog decides it

A `RoutedKey.Editor` member, one row:

```csharp
new(
    new KeyPattern(KeyKind.Character, Ctrl: true, Alt: false, Text: "g"),
    RoutedKey.Editor,
    KeyContext.None,
    "Open the draft in your editor"
)
```

**Why `None`.** `None` means every prompt state, bash mode included, because the draft *is* the draft — a `bash` line is text like any other, and refusing to edit it would be a special case with no user behind it. The states that must not see `Ctrl+G` (approval, picker, transcript viewer, `?` overlay, secret and trust prompts) never reach the router: the session returns from its approval/picker/viewer/help branches first. `Composing` is gated upstream the same way, so the row can say "every prompt state" without lying.

**Placement.** Beside `Ctrl+S`/`Ctrl+R`, not among the line-editing rows: the `?` overlay lists rows in catalog order, and a session action should read next to the other session actions.

**Who wins.** Routing is first-match over the catalog, so the row wins over anything appended after it. Two consequences are worth stating because they are the question:

- A **terminal** that never delivers `Ctrl+G` (`Ctrl+G` is `BEL`, `0x07`; some terminal emulators and tmux configurations consume it) removes the capability there. Nothing in the process can recover a byte that never arrives, and inventing a second chord would put a key's meaning outside the catalog.
- An **extension or keybindings file** that wants `Ctrl+G` must *replace* this row, not append a second one: `No_two_rows_are_the_same_binding` and `Earlier_rows_are_never_shadowed_by_later_ones` already reject a duplicate in an overlapping context, so the attempt fails a test rather than silently changing what the key does. That is the whole point of the row being data.

Alternative rejected: handling `Ctrl+G` in the session's key switch beside the router. It is how the drift started the first time, and the overlay could not list it.

### D2 — The suspend seam belongs on `IConsoleIO`, because two readers on one tty split the editor's keys

The session holds the terminal in raw mode and `SystemConsoleIO.ReadKeysAsync` runs a pump that `poll`s and `read`s file descriptor 0 continuously. Handing the terminal to a child while that pump still runs would leave two readers on one tty: bytes go to whichever read wins, so the editor would lose keystrokes and the session would receive garbage. Suspension is therefore not a paint concern; it is an input-ownership concern.

`IConsoleIO` gains one member returning a scope:

```csharp
IDisposable SuspendTerminal();
```

Taking it first quiesces the pump (no further reads; the key channel stays open so the session's enumeration is not disturbed), then restores the terminal mode the process started with (termios on Unix, the saved console mode on Windows), and flushes the VT decoder's partial-sequence buffer. Disposing it clears that buffer again, re-enters raw mode, restarts the pump and lets the live area paint.

**Why flush both ways.** A lone `Esc` typed a moment before `Ctrl+G` is still waiting to see whether a sequence follows. If the buffer survives the handover, the editor's first byte completes *our* escape sequence and both sides see a wrong key. Cheap to state, very annoying to debug.

**Why the pump is quiesced rather than completed.** Completing the channel ends the session's `await foreach` and would need a new enumeration on resume — a second lifetime for one read loop. Quiescing keeps one lifetime and one channel; only the blocking read moves aside.

`FakeConsoleIO` records the transitions (`Suspended`, `Resumed`, the mode at each point), so the test asserts that the child saw cooked mode and that no key was read while it ran — without a terminal.

Alternative rejected: a `RunChildAsync(ProcessStartInfo)` on `IConsoleIO`. It would put process spawning in `Lunate.Tui`, and the child's exit code, output and timeout are the session's business, not the console seam's.

### D3 — The editor contract: resolve, write, read back, restore

Resolution is one pass, first non-empty wins: `$VISUAL`, `$EDITOR`, then `vi` on POSIX and `notepad` on Windows. A value is trimmed and split on whitespace — program first, the rest arguments — with **no shell and no quote interpretation**. `EDITOR="vim -u NONE"` works; `EDITOR='"/path with space/vim"'` does not, and documenting one rule beats a half-shell that surprises on Windows.

**Why no shell.** A shell in the way turns a configuration value into a command language: quoting rules differ per platform, and a value with `;` in it would run something the user did not name. One split rule, the same everywhere.

The temp file: `Path.GetTempPath()` with a `lunate-draft-<random>` name, written UTF-8 without a BOM, created with owner-only permissions on POSIX (the same reasoning as `auth.json`), deleted in a `finally`.

Read-back: CRLF → LF, and **exactly one** trailing newline removed. Editors append a final newline as a matter of course; removing all of them would eat a deliberate blank line, and removing none would make every edit add an empty last line to the box.

**Cursor on success is the end of the text.** The old offset means nothing in text the user rewrote in another program, and "somewhere sane" is better than "somewhere stale". On failure the pre-edit draft *and* pre-edit cursor are restored, which is only possible because the session snapshots both before writing the file.

**Empty draft opens an empty buffer.** Composing a long message from nothing is half the reason for an editor; refusing on emptiness would make the key useless exactly when it is most wanted, and would need a rule for "empty but the user wanted to clear it". An editor that leaves the buffer empty leaves the draft empty — no "nothing changed" special case.

### D4 — Failure is reported, never silently fallen through

If `$VISUAL` is set and cannot be launched, the command **fails and says so**; it does not try `$EDITOR`. Falling through would run an editor the user did not configure and quietly edit their draft in it — the kind of surprise that is worse than the failure. A variable that is *unset or blank* falls through, because that is what "unset" means.

Every failure mode posts exactly one notice, and the notice carries the fact that fixes it:

| Situation | Draft and cursor | Notice |
| --- | --- | --- |
| `$VISUAL` names a missing program | untouched | `VISUAL="nvim-preview": no such program — draft unchanged` |
| Program starts, exits 1 | untouched | `editor exited 1 — draft unchanged` |
| Process start throws (`Win32Exception`) | untouched | the exception's message, prefixed `could not start <program>:` |
| Read-back fails | untouched | `could not read the edited draft: <reason>` |

Nothing here can crash the session: the child runs inside the same `try`/`finally` that resumes the live area and deletes the file.

### D5 — A draft that changed under the editor loses the prompt, not its text

While the child runs, no key reaches the session, so an ordinary user cannot change the draft. Programmatic paths can (an extension's contribution, a queue take-back, a future background write). The rules that were available:

1. Editor's text always wins — a change that arrived later is silently reverted.
2. The newer draft wins and the editor's text is discarded — the user loses what they just typed in the editor.
3. The newer draft wins the prompt and the editor's text goes to the stash — nothing is lost, and the prompt shows the newest state.

(3), with one notice naming where the edited text went. It reuses the stash slot `Ctrl+S` already ships rather than inventing a second holding area, and it is testable without a race: the fake case writes into the draft while the scripted child is "running".

### D6 — The approval selection is prompt state, not focus

`ApprovalPromptModel` gains `Selected` (an index over the three choices in display order) and the model answers keys itself: Up and Down produce a moved model, Space produces a decision for the selected choice, and `y`/`n`/`a`/Enter/Esc keep their current decisions. The session re-posts the prompt on a move.

**Why state and not focus.** A focus lives in the renderer or the window and dies on the next paint; the approval panel is repainted whenever a turn event, a spinner tick or a resize lands, which is to say constantly. A selection stored in the model survives all of it and is restored by the same code path that draws the panel at a new width. This is also the honest answer to "does the selection survive a redraw": it is not that the renderer remembers, it is that there is nothing to remember.

**Why every new prompt starts on `[y]es`.** The shipped scenario already pins it (`Down`, `Down`, `Space` → always), and a remembered selection across prompts would make Space mean something different on the next question without the user looking at it. The default is not a hazard: `Space` is a deliberate key, `Enter` still denies, and nothing approves on a stray return.

**Why `j`/`k` are not added.** The overlay and the pickers use them because their lists are long and vim-shaped. A three-choice question prints its own `[y]`/`[n]`/`[a]`, and adding two more letters would need a spec sentence each while making `k` ambiguous with a future "kill" binding in the panel. Up/Down are the keys the requirement names; that is enough.

### D7 — Wrap, mark it in text, and let 40 columns decide the layout

**Wrap, not clamp.** The other lists clamp because wrapping a filtered list is disorienting. Three fixed choices are all on screen, so wrap cannot hide anything and a dead key at the end reads as a broken key. Up on `[y]es` lands on the last choice; Down on the last lands on `[y]es`.

**The marker is text.** The autocomplete already marks its selection with a `"> "` gutter precisely so the selection survives `NO_COLOR`; a reverse-video or colour-only marker would vanish under `NO_COLOR` and in the ASCII fallback, which is the one case where a selection must still be visible. `> ` before the selected choice's bracket.

**The numbers.** `[y]es  [n]o  [a]lways this session` is 34 cells; with the marker it is 36 everywhere it can sit, so the three choices stay on one row at 40 columns, which is the width the requirement and its golden frames test. Below 36 the block stacks one choice per row with the gutter column kept, so the marker does not jump horizontally. Below the longest stacked row (22 cells) a label clips only *after* its key letter — a torn label would hide which decision the row is, and a hidden decision is the one thing the panel may not do.

**Height.** The choice list is the three fixed decisions, so the block's height is at most "question + one row" and the frame can always show it once it can show the question. The existing trim order (`LiveAreaTrim`: transient lines, then detail, then headline) stops above the choice rows, and the requirement now says so. A fourth choice is a layout change the requirement refuses, which is the honest answer to "what happens when the list grows past the panel height": it cannot grow, and if it ever does, the layout has to change on purpose rather than degrade quietly.

### D8 — The drift check runs both ways, over keys, with a named exemption table

Both directions, because they catch different mistakes: catalog→doc catches a binding nobody documented (the six keys missing today), doc→catalog catches a row that documents a key nobody routes (the ghost that `Ctrl+P`'s removal would have left).

**Why keys and not intents.** Merged cells hold keys with different intents (`Ctrl+A` start-of-line next to `Ctrl+E` end-of-line), so an intent-per-cell rule would either force one row per intent — a worse table, since these belong together — or need a mapping table beside the doc that itself drifts. The semantics are already pinned by `Every_row_is_reachable` (row → intent) and shown by the overlay (row → description). The drift check owns coverage; the catalog's own tests own meaning. Saying that out loud in the requirement is what keeps a future reviewer from "improving" the check into a prose comparison.

**Parsing.** Only the table under `**Key bindings (one meaning each)**`, only the Key column, cells split on `/`, backticks and spaces trimmed, spelling exactly `KeyPatternFormatter`'s (`Ctrl+G`, `Esc`, `Up`, `Ctrl+_`). A key named inside an Action cell is *not* documented — that rule is what stops "the prose still mentions `Ctrl+J`" from passing for a missing row. The section heading, the table and its `| Key | Action |` header must exist, or the test fails: otherwise deleting the table is the cheapest way to make it green.

**Exemptions** live in one table in the test, each with its reason:

| Key | Why it has no catalog row |
| --- | --- |
| `y`, `n`, `a` | the approval panel owns its keys; the session returns before the router while a prompt is pending |
| `?` | gated on an empty idle prompt, a condition the context flags cannot express — it stays a character while typing |
| `Ctrl+P` | deliberately unbound since #124; documented so a reader knows it does nothing |

The requirement names the list, so adding a fourth exemption is a spec change, not a convenient edit in a test file.

**Failure text.** Each direction names the key, the direction and the row that carries it, because the fix differs: a gap means "add a row to the guide", a ghost means "the doc claims a key nothing routes".

### D9 — What the guide table gains, counted

Today: 25 documented keys, 30 catalog keys. The check cannot go green without these six (all already routing, none documented): `Ctrl+J`, `Shift+Enter`, `Ctrl+Enter`, `Ctrl+Delete`, `Ctrl+Left`, `Ctrl+Right`. Three rows are added: `Ctrl+G` (new row, new catalog row), `Ctrl+P` (unbound, exempt) and `y` / `n` / `a` (approval decisions, exempt). Six keys land in cells that already describe them: the newline keys join `Alt+Enter`, `Ctrl+Delete` joins `Ctrl+W` / `Alt+D`, and `Ctrl+Left` / `Ctrl+Right` join `Alt+B` / `Alt+F`. That is six row edits — three new rows and three cells extended — with no reordering, and no new prose beyond the approvals paragraph's selection sentence.

## Risks

- **`Ctrl+G` is `BEL` on the wire.** Terminals and tmux configurations that consume it make the key unreachable there, and there is no in-process fallback. Accepted for now; the follow-up is a `/editor` command (see open questions), not a second chord.
- **The suspend scope touches the most delicate plumbing in the TUI.** The fake console proves the ordering and the mode transitions, but only a real terminal proves the handover. The manual matrix (T-33) covers vim, nano and `notepad`; the scope's contract is narrow enough that a wrong implementation fails the fake-console assertions first.
- **Editors that do not wait for the file.** `code` without `--wait`, and some Windows editors, return immediately, so the read-back races the edit. No notice can detect it. Mitigation: the guide's `Ctrl+G` row says the program must wait (`code --wait`, `vim`), and the temp file's deletion is the visible symptom when it does not.
- **The marker's two cells** shift the choices row from 34 to 36. Golden frames for the 40-column panel change; regenerate them once, deliberately, in the layout commit.
- **`IConsoleIO` is public.** The suspend member and `ApprovalPromptModel`'s new members need `PublicAPI.Unshipped.txt` entries; a missed entry fails the API gate, not the review.

## Open questions

1. **Is exempting `?` acceptable, or should the catalog grow an `Empty` context?** The card named two exemptions (the approval letters, `Ctrl+P`); the shipped doc table also documents `?`, which has no catalog row because its gate is "the prompt is empty", not a context flag. Recommendation: keep the exemption now, exactly as tabled, and treat a `KeyContext.Empty` flag as the cleanup that would let `?` (and an "editor only on an empty draft" rule, if anyone wants one) become catalog rows. Adding the flag here would touch routing, the overlay and every row's context for one key.
2. **Does `Ctrl+G` need an Alt-free or terminal-proof route?** Recommendation: not in this change. If terminals eating `BEL` become a support burden, add a `/editor` command that runs the same flow — same code path, one registry entry — as its own card, and keep the catalog row as the only key.
3. **Default selection `[y]es` or `[n]o`?** Recommendation: keep `[y]es`; the shipped scenario (`Down`×2 → always) pins it, and Space-approves is deliberate while Enter denies. Flipping to `[n]o` would mean editing that scenario and re-teaching the muscle memory the panel just gained.
4. **Changed-draft rule: newer draft keeps the prompt, editor's text to the stash.** Recommendation: as designed. The alternative (editor always wins) silently reverts whatever wrote later, and the other alternative (discard the editor's text) throws away typing the user just did.

## Deferred

- **The `features-keys.tape` demo (item 4 of `t_7a383140`)** — visible-key recordings belong to the video-regeneration card; nothing here adds or changes a tape.
- **`/editor`** — the terminal-proof fallback for `Ctrl+G` (open question 2).
- **`KeyContext.Empty`** — the flag that would let `?` and empty-draft-only bindings be catalog rows (open question 1).
- **A keybindings file and extension bindings** — P8, `t_7b529518`; this change only makes sure a replacement row is the supported way to take `Ctrl+G`.
- **`j`/`k` on approvals, and a fourth choice** — no.
