# Design: Tool blocks and diff rendering (T-20)

## Structure

New files in `src/Lunate.Tui/`: `ToolBlocks/ToolBlockModel.cs` (public), `ToolBlocks/ToolBlockRenderer.cs` (public), `ToolBlocks/ToolArgsSummary.cs` (internal), `ToolBlocks/DiffRenderer.cs` (internal), `ToolBlocks/OutputExcerpt.cs` (internal truncation). Goldens under `tests/Lunate.Tui.Tests/fixtures/toolblocks/`. No new packages; reuses the T-19 golden convention (`LUNATE_TUI_UPDATE_GOLDENS=1`).

## Model (primitives only)

```text
ToolBlockModel(
    string ToolName,        // "edit", "bash", "cs_rename", ...
    string ArgsSummary,     // pre-summarized or raw args JSON; renderer summarizes when raw
    ToolBlockStatus Status, // Running | Ok | Error
    string? Output,         // tool output text (may be null while running)
    ToolDiffInfo? Diff)     // Path, MatchTier, UnifiedDiff (edit/write only)

ToolDiffInfo(string Path, string MatchTier, string UnifiedDiff)
```

`Lunate.Tui` never references `Lunate.Coding`; T-22 maps `EditDetails` (Coding) onto `ToolDiffInfo` at the wiring seam. `MatchTier` stays a string (v1 values `exact` / `normalized`; the renderer styles unknown values neutrally).

## Rendering decisions

- **Header**: `tool <name> <argsSummary>` + status suffix — `ok` green, `failed` red, `running…` dim (the live area reuses the same model later).
- **Output excerpt** (`OutputExcerpt`): first 8 lines + last 8 lines, middle replaced by `… N lines hidden …` (dim, invariant number); outputs ≤ 17 lines render whole; a single trailing newline is trimmed. Constants in one place, pinned by unit tests.
- **Diff panel** (`DiffRenderer`): parses the unified text — `---`/`+++` file headers dim; `@@` hunk headers cyan-dim; `+` lines green; `-` lines red; context dim; a leading `match: <tier>` label: `exact` dim, `normalized` **yellow** (visible fallback per the guide). Borderless (scrollback copy-friendly, same reasoning as T-19 fences). Unknown/empty diff → omitted entirely; a diff present with empty text renders nothing extra.
- **Escaping**: every user-derived string through `Markup.Escape` (args summaries, output lines, diff lines, tier labels) — same rule and falsifiers as T-19.
- **Arg summaries** (`ToolArgsSummary`): `read`/`write`/`edit` → `path`; `bash` → `command` (first line, elided at ~60 chars); `cs_*` → symbol/name field; unknown tools → first line of the args JSON that carries a string value, else the raw args trimmed; invalid JSON → the raw string. Pure function; adversarial tests (missing fields, wrong types, huge strings).

## Snapshots

`TestConsole` (plain-text profile) goldens under `tests/Lunate.Tui.Tests/fixtures/toolblocks/`: `read-ok.txt`, `bash-ok.txt`, `error.txt`, `long-output.txt` (truncation), `running.txt`, `edit-diff-exact.txt`, `edit-diff-normalized.txt`, `write-diff.txt`, `no-output.txt` (empty output), `escaping.txt` (bracket text in args/output/diff stays literal). Targeted ANSI assertions: green/red diff lines, yellow normalized label, red failed status.

## Out of scope

Wiring to the event stream / live area / scrollback (T-22); approval prompt and footer (T-21); syntax highlighting inside diffs (v1: plain red/green, no per-language highlighting); wrapping policy for very wide diff lines (terminal width handling stays T-33).

## Deviations

(Filled during apply; empty at proposal time.)
