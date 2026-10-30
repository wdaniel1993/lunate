# Card: Themes and keybindings in /settings (issue #322)

Reproduction of symptom (rule 2a): `src/Lunate.Coding/InteractiveSession.Settings.cs` — EditableKeys
has only approval/thinkingFull/toolDetails/toolResultLimit; ShowSettings/ShowOneSetting/KnownKey
have no theme or remap entries. `/settings` cannot choose a theme or remap a key today. Confirmed
by code read; will be pinned by a red test (`/settings tui.theme mono` → "no such key") before the fix.

## Design decisions

- Settings file (schema 2) gains a `tui` section: `"tui": { "theme": "default|ansi16|mono", "keys": { "<binding-id>": ["<keyspec>", ...] } }`.
  - Display names `tui.theme` / `tui.keys`, matching the `tools.extra` dotted-key convention.
  - The active `tui-customization` OpenSpec change's delta names `tui.theme` for theme selection; the
    user-file keymap (keymap.json/theme.json) parts of that change remain open — this card is the
    /settings slice (recorded on the PR).
- Theme = a colour-depth policy applied at the session layer, reusing existing capability machinery:
  - `default` → detected colour system (unchanged visuals, zero behaviour change);
  - `ansi16` → the 16-colour (high-contrast) system everywhere;
  - `mono` → NoColors everywhere (like NO_COLOR). NO_COLOR still wins because Detect already
    returns NoColors.
  - Application points: effective `TerminalCapabilities` computed once per session after settings
    resolve; pushed to LiveArea, MarkdownRenderer, ToolBlockRenderer, TranscriptViewer; other colour
    call sites take the effective colour. Footer/status line paints muted (not plain) in colour modes.
- Key remaps: `TuiTheme`-independent. `KeyString.TryParse` (Tui, internal) parses "Ctrl+J"-style specs
  → KeyPattern. `KeyBindingCatalog.Remap(id, patterns)` replaces the rows' patterns in place
  (single declaration — id, intent, context, description, position all keep; no second row).
  SettingsStore validates ids against the catalog (unknown id → nearest ids in the error) and key
  specs via KeyString at read and write time. Session applies remaps after Resolve.
- Persistence: `SettingsStore.WriteTuiSettings(theme, keys, path?)` read-modify-write like
  WriteFavorites (atomic replace, validations through the same parser).

## Slices (test-first each)

1. Tui theme contract + key-string parser + catalog remap (Lunate.Tui.Tests). Commit 1 → push → draft PR.
2. Colour-mode application through the TUI surfaces + footer tone (Lunate.Tui.Tests).
3. SettingsStore `tui` section + writer + startup application (Lunate.Coding.Tests).
4. `/settings` edits: tui.theme, tui.keys — apply immediately, persist, show in /settings;
   remapped key driven through the session loop; restart-survival tests. Update the
   InteractiveSessionSettingsTests "editable here" assertion.
5. docs/guide.md updates (settings keys, /settings paragraph), csharpier, narrow gate, PR comments.

## Rules honoured
- No new NuGet packages; InvariantCulture for all technical output.
- No openspec/ edits; AGENTS.md untouched. No subagents (card forbids).
- Draft PR after first commit: body first line `Refs #322`.
- Every dotnet command: MSBUILDDISABLENODEREUSE=1 and -nodeReuse:false; build only the touched test
  project; filter test runs.
