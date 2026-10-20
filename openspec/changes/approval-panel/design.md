# Design — approval panel

## D1. The model

`ApprovalPromptModel` gains the detail it must show, built by the session from the `ApprovalRequest`:

- `ToolName`, `Asker`, and the existing `ArgsSummary` (the one-line summary used in compact contexts);
- `Detail`: an ordered list of display lines prepared per tool — `bash`: the command (wrapped, bounded to 6 lines with a `…` marker); `edit`/`write`: the path plus the diff rendered by the existing `DiffRenderer` (bounded like a tool block); `read`: the path and range; other tools: the path/URL/key arguments from `ToolArgsSummary`'s per-tool extractor; unknown tools: the raw arguments (bounded);
- `WorkspaceRoot` when it differs from the session's working directory.

The detail is prepared in the Coding layer (it owns the summarizers) and passed as plain lines, so the Tui renderer stays a pure layout component and the model needs no JSON.

## D2. The panel layout

Three parts, top to bottom, in the live area above the input:

1. **Headline** — `⚠ <tool>  <what>` in the warning colour: the tool name and the shortest concrete identifier (the command's first line, `path  +N −M`). ASCII fallback `!`.
2. **Detail** — the prepared lines, muted, indented two spaces, bounded to `MaxDetailLines` (6) with the `… +N lines` marker; clipped, never wrapped past the width.
3. **Question** — `Allow <tool>?  [y]es  [n]o  [a]lways this session` with the key letters in the accent colour; a subagent asker prefixes `<label> asks: `.

Height: the panel is part of the live frame and counts against the terminal height like the autocomplete list — the frame cap trims the detail first, then the headline, never the question.

## D3. The narrow band

Below 60 columns the headline keeps `⚠ <tool>` + a clipped action, the detail drops to its first line, and the question moves to its own line as `[y]es  [n]o  [a]lways this session` with `Allow <tool>?` above it when it would clip. **Every choice stays visible at 40 columns** — the acceptance test.

## D4. Keys and semantics

Unchanged: bare `y`/`n`/`a` decide, Enter denies, everything else is ignored (the existing `Decide`). The panel does not capture other keys; the input stays inert while it is open (existing rule).

## D5. Tests and media

- Renderer goldens per band (40/60/80/120) and colour mode (truecolor/256/16/NO_COLOR) for: a bash command, an edit with a diff, a read, an unknown tool, a subagent asker.
- Composition test at 40 columns on the real host: an edit approval shows the diff and all three choices.
- A tape `features-approval.tape`: a turn that triggers an edit approval, the panel with the diff, `a` pressed.
- Docs: the guide's approvals paragraph gains the panel description.
