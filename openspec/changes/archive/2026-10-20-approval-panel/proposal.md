# Proposal — approval panel

## Why

Permission confirmations are a single line clipped at the terminal width: `ApprovalPromptRenderer.PlainText` → `CellText.Clip`. On a phone-sized terminal the user sees something like `Allow bash dotnet build?  [y]es  [n]o  [a]lways t` — truncated mid-option, and with no sight of what will actually run. The report: "confirmations of permissions still are not fully shown".

The signed-off visual spec (§3.9) already specifies a two-row prompt — a warning headline naming the concrete action and the question with its choices — but only the second row was built, and even that without detail.

The data exists: `ApprovalRequest` carries the full arguments as they will be executed and the asking run's workspace root, so a panel can show the real command or the real diff before the user decides.

## What changes

- **A multi-line approval panel** in the live area: a warning headline with the concrete action (`⚠ bash  dotnet build` / `⚠ edit  TodoList.cs  +5 −0`), a detail block bounded like tool blocks (the full command for bash; the path and diff for edit/write; the path and arguments for others; the workspace root when relevant), and a question line `Allow <tool>?  [y]es  [n]o  [a]lways this session` with the key letters accented.
- **The choices are never clipped**: below 60 columns the choices move to their own line and the detail truncates first; every option stays visible at any width.
- **The deciding keys are unchanged** (bare `y`/`n`/`a`; Enter denies; everything else is ignored), and the subagent asker label stays.
- Renderer goldens per band and colour mode, a composition test at 40 columns asserting all three choices are visible, and a VHS tape showing an edit approval with its diff.

## Non-goals

- Changing what the approval policy asks about, or the always-memory semantics.
- The trust prompt's narrow-width clipping — a separate card (`t_35821c69`); the shared panel primitives may serve it later.
