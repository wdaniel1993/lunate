# Input box and as-you-type autocomplete

## Why

The input line is the TUI's most-used surface and the least finished. It renders as bare prefixed lines with no visual boundaries, no placeholder, and no hint of what it accepts; Tab completion exists but is invisible until pressed — the whole-input `/word` rule only, with candidates arriving as a dim notice after the fact. The visual study's step-0 sign-off puts the input box (rules above and below, placeholder, growth to 8 lines) and as-you-type autocomplete (one registry, fuzzy filter, ≤8 rows above the input) in the epic's card 3.

## What changes

- **Input box chrome**: a full-width rule above and below the input at ≥80 columns, the placeholder `Ask anything, / for commands` when the input is empty, growth to 8 lines then scrolling (the layout already supports this; the box makes it visible), and the status footer moves below the box per the layout skeleton.
- **Autocomplete**: typing `/` at the start of the input opens a list of the built-in commands, `/skill:<name>` entries and extension commands, each with a short description; typing `@` opens the workspace-file list (the lazy index). One registry built from the command table, the skill catalog and the extension manifests — no separate hard-coded lists. Fuzzy-filtered as you type; ↑/↓ moves the selection, Tab or Enter accepts it, Esc closes; at most 8 rows above the input; descriptions drop below 60 columns; file rows show the muted directory part.
- **Key precedence** while the list is open: ↑/↓ drive the selection (history otherwise), Enter accepts (submit otherwise), Esc closes the list (cancel otherwise); Shift+Enter / Alt+Enter / Ctrl+J keep inserting newlines.

## Scope

- New pure fuzzy matcher and registry model; the live-area state carries the open list; the renderer draws the box and the rows; the session composes the registry and drives the state.
- The old Tab-only completion behaviour (whole-input LCP, candidate notices) is replaced by the list; the file index itself is unchanged and keeps its laziness, ignore rules, bounds and budget.
- Extension commands are listed from the manifests; their invocation path is a separate concern (today an unknown slash word is a notice) and is not changed here.
- No new packages.

## Acceptance

Snapshot tests per breakpoint and colour mode; a production composition test (the real host renders the box, opens the list, accepts an entry); a VHS tape driven by the replay client; the startup budget unchanged (the registry is cheap; the file index stays lazy). OpenSpec change per the design; the kanban card links to it.
