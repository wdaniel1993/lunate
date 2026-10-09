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

## T-22 wiring seam

`EditDetails(Path, FirstLine, LastLine, MatchTier, Diff)` maps to `ToolDiffInfo(Path, MatchTier, Diff)`;
`WriteDetails(Path, Created, Lines, Diff)` maps to `ToolDiffInfo(Path, "exact", Diff)` (a write applies
the content verbatim, no fallback). `ToolBlockModel(ToolName, ArgsSummary: raw args JSON, Status,
Output, Diff)` is assembled from the `ToolCallStart`/`ToolCallResult` events at the same seam.

## Deviations

- `ToolDiffInfo.Path` is carried but not printed in v1: the unified text already carries the paths in
  its `---`/`+++` headers, so the panel would only duplicate them; T-22 can use the field in the live area.
- `WriteDetails` has no match tier on the wire. Writes map to `exact` (verbatim content, no fallback)
  so the label requirement holds; `DiffRenderer` still omits the label when `MatchTier` is empty or
  whitespace, and styles unknown tiers neutrally.
- Lines before the first `@@` that are neither `--- `/`+++ ` headers nor a hunk are rendered as dim
  context instead of being dropped: malformed diffs never lose text and never throw.
- The hidden-lines marker is pinned verbatim from the design as `… N lines hidden …` (U+2026
  ellipses) with an invariant count; `OutputExcerpt` owns the constants and the marker format.
