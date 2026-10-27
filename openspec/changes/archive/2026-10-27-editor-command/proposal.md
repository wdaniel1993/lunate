# The `/editor` command

## Why

The external-editor round-trip shipped with one entry point: `Ctrl+G`. That key is `BEL` (`0x07`), which some terminal emulators and tmux configurations consume before the process ever sees it, and an extension or keybindings file that claims the chord replaces the catalog row — the supported way to take a key removes the capability with it. Nothing in the process can recover a byte that never arrives, so the one deliberate editor flow in the session is unreachable for those users. The key change documented that limitation in the guide's `Ctrl+G` row and deferred the fix to its own card; this is that card.

## What Changes

- A built-in slash command `/editor` opens the draft in the user's external editor: the same round-trip `Ctrl+G` runs — `$VISUAL`/`$EDITOR`/platform default resolution, the temporary file, the terminal handover, the editor-wins reconciliation and one notice on failure.
- The command is registered in the built-in descriptor table, so `/help` lists it with the other session commands and `/` completion offers it; there is no second list to keep in step.
- The guide's `Ctrl+G` row names `/editor` as the reachable alternative when the terminal does not deliver the key.
- No new editor behaviour: resolution order, read-back rules, failure notices and temp-file lifecycle are unchanged, so no new failure mode arrives with the command.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `tui`: `/editor` joins the slash-command requirement, and the external-editor requirement states that the same round-trip is reachable as a command and what draft it acts on.

## Impact

`Commands.cs` gains one descriptor, `InteractiveSession.BuiltinCommands.cs` one dispatch case; `InteractiveSession.Editor.cs`, `DraftEditor` and the TUI's suspend seam are untouched. `docs/guide.md`'s key table and editor notes change. Tests: the command's round-trip over a scripted child, the registry's descriptor/switch parity (already enforced), and the help/completion listing.
