# Design — add-file-tools (T-12)

## Where the code lives

`Lunate.Coding` (the app layer; `Lunate.Agent` stays the tool-contract library). New files: `Workspace.cs`, `ReadTool.cs`, `WriteTool.cs`, `WriteDetails.cs`, internal `TextFile.cs`, internal `LineDiff.cs`. Tests in `tests/Lunate.Coding.Tests/` (which already has `InternalsVisibleTo`).

## Workspace: canonical resolution and the boundary

- **`Workspace`** is created from the working directory plus optional extra roots (`Workspace(cwd, extraRoots)`; the CLI's `--allow-path` maps to `extraRoots` in T-17). One shared implementation for every file tool — `edit` (T-13) reuses it unchanged.
- **`TryResolve(string path, out ResolvedPath resolved, out string error)`** returns the canonical absolute path plus the workspace-relative display form (forward slashes on all platforms — the model sees portable paths).
- **Algorithm** (pinned):
  1. Reject null/whitespace with an instructing error.
  2. Combine relative paths with the working directory; `Path.GetFullPath` (lexical normalization first — over-refusal in the safe direction is fine).
  3. Canonicalize: walk the path's components from the root; for every existing component that is a link, replace it with `ResolveLinkTarget(returnFinalTarget: true)`. The non-existing tail (write targets) is appended after the deepest existing ancestor is canonicalized. The roots themselves are canonicalized the same way (the working directory may sit behind a symlink).
  4. **Inside check**: the canonical candidate equals a canonical root or sits under it. Comparison: `OrdinalIgnoreCase` on Windows and macOS, `Ordinal` on Linux (guide: boundary checks are case-insensitive on Windows and default macOS file systems).
  5. Failure: error naming the path, the resolved target and the allowed roots — instructing text per the tool contract.
- **Symlink rule**: the *final target* decides. A symlink inside the workspace pointing outside is refused; one pointing inside is allowed. Links are never followed silently past the boundary.
- **TOCTOU**: the check runs at call time; this is a guard rail for a single-user CLI tool, not a sandbox. ADR-0016 records this explicitly.

## `read` tool

- **Schema** (hand-written JSON): `path` (string, required), `offset` (integer, "1-based first line, default 1"), `limit` (integer, "maximum lines, default 2000, max 2000").
- **Flow**: resolve (outside → error) → directory → `X is a directory; use bash ls` → missing → `file not found: X` → binary check (NUL byte in the first 8,192 bytes) → `X is a binary file (N bytes); read handles text files` → read the file once as UTF-8 (BOM stripped; invalid bytes become U+FFFD).
- **Validation**: `offset < 1` and `limit < 1` are errors; `offset` past the end is an error naming the total (`offset N is past the end of X (M lines)`); `limit > 2000` clamps to 2000.
- **Output format** (pinned, exact): one line per source line, `{n,6}|{text}` — line number right-aligned in a field of `max(6, digits(total))`, pipe, no space; numbers formatted culture-invariantly. Lines are split on `\n` with one trailing `\r` stripped (display normalization only; the file is never touched). Empty file: `[empty file]`.
- **Footer** (only when more lines remain): `[lines {first}–{last} of {total}, use offset to continue]` (en dash), appended as the last line.
- Files are read fully into memory for counting and the window; acceptable for text files (the 5 MB corpus case is far below any concern). A streaming reader can replace this if the eval shows pain.

## `write` tool

- **Schema**: `path`, `content` (both required strings).
- **Flow**: resolve (outside → error) → existing directory → `X is a directory` → create parent directories as needed → write the content **exactly as given**: UTF-8 without BOM, no newline munging (CRLF stays CRLF, no trailing newline added or removed), file permissions preserved on replace (overwrite in place), default permissions for new files.
- **Result**: `wrote {n} lines to {relative path} ({created|replaced})`; `n` = 0 for empty content, otherwise the count of `\n` plus one if the content does not end with `\n`.
- **`Details`**: `WriteDetails(string Path, bool Created, int Lines, string Diff)` — `Path` is the workspace-relative display form. `Diff` is a unified diff (`--- a/…`, `+++ b/…`, `@@` hunks, 3 context lines) from an internal LCS `LineDiff`; a create shows all `+` lines. UI-only, never sent to the model (tool contract). This internal diff is v1 scope: when T-19 builds the TUI's Myers diff, the renderer uses that one and this helper is retired (guide: "Diffs — own Myers line diff — Lunate.Tui").
- All errors are `ToolResult(IsError: true)` with instructing output text; the model learns what to do next.

## Testing strategy

- **Case sensitivity**: tests probe the temp file system (create `Case` + `case` probe) and assert accordingly, so the same suite is honest on case-sensitive and case-insensitive volumes.
- **Symlinks**: `Assert.Skip` when link creation is unavailable (unprivileged Windows runners); otherwise full inside/outside coverage.
- **Exact formats**: line format, footer, result lines and errors are asserted as exact strings; content round-trips assert byte exactness (CRLF, BOM-less, trailing newline state, empty file).
- A `TempDirectory` test helper mirroring `Lunate.Agent.Tests`.

## Deliberate non-goals

- No registry wiring or CLI integration (T-16/T-17 own the app assembly).
- No approval-policy logic (T-16); the `IToolApprover` seam already exists.
- `Lunate.Coding` stays untracked by PublicAPI like before (app assembly; the tracked libraries are Ai, Agent, Protocols, Roslyn, Tui). Revisit if extensions consume Coding types.
