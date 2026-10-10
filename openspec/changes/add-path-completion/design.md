# Design: path completion and file index (T-53)

## Structure

- `src/Lunate.Coding/FileIndex.cs` (new): the workspace index — lazy async build, `.gitignore` evaluation, bounds, prefix query.
- `src/Lunate.Coding/GitIgnore.cs` (new): pattern parsing + translation + matching (the pure engine; no filesystem).
- `src/Lunate.Coding/WorkspaceFiles.cs` (new): the filesystem seam + production implementation.
- `src/Lunate.Coding/InteractiveSession.Commands.cs`: `ApplyCompletion` gains the `@token` path branch.
- Tests: `tests/Lunate.Coding.Tests/` — `GitIgnoreTests` (matrix), `FileIndexTests` (bounds, ordering, laziness via the seam), `PathCompletionTests` (token scan + session flow), `PathIndexBudgetTests` (trait `Category=Perf`, real FS).
- `scripts/verify.sh` / `scripts/verify.ps1`: main passes exclude `Category=Perf`; a new "path index budget" step runs only it.

## The seam (pinned)

`IWorkspaceFiles` — `IReadOnlyList<WorkspaceEntry> Enumerate()` returning relative paths (files and directories) and `string? ReadAllText(string relativePath)`; `WorkspaceEntry(string Path, bool IsDirectory)`. Production `SystemWorkspaceFiles(workspaceRoot)`: recursive walk, directory symlinks NOT followed, `.git` directories skipped during the walk (never indexed). The index itself is constructed by the session on first need — never at startup.

## Ignore engine (pinned)

`.gitignore` parsing: blank lines and `#` comments skipped; `!` negation; trailing `/` = directory-only; trailing spaces trimmed (escaped trailing space unsupported, documented); a pattern with a non-trailing `/` is anchored to its `.gitignore` directory, otherwise it matches the basename at any depth. Glob translation to regex: `*` → `[^/]*`, `?` → `[^/]`, `**/` → any depth, `/**` → everything below, `[...]` classes passed through (escaped where needed), everything else regex-escaped. Matching: files evaluated against every `.gitignore` from the root down — deeper files win, within one file the LAST matching pattern wins (so `!` re-includes). An excluded directory prunes its subtree; files inside it cannot be re-included (git-documented behaviour, kept).

## Index (pinned)

- Build: lazily triggered (`EnsureStarted`), runs on a background task; result published atomically; `IsReady`, `IsTruncated`, and `IReadOnlyList<string> Match(string prefix, int max)` (ordinal prefix match over sorted entries; directories carry a trailing `/`).
- Bounds: entries sorted (ordinal) then capped at **200,000**; over-cap sets the truncation flag. Sorting before capping makes the result deterministic regardless of walk order.
- Skip rules: `.git` always; ignored paths per the engine; symlinked directories not followed (files reached through symlinks are not indexed either — the walk does not descend).
- Case: ordinal matching (documented; matches the filesystem-agnostic promise of identical behaviour).

## Session wiring (pinned)

`ApplyCompletion` order: (1) today's single `/word` rule unchanged; (2) otherwise, scan back from the cursor over non-whitespace — if that token starts with `@` and the cursor is at its end, complete `token[1..]` against the index: not ready → `EnsureStarted()` + dim notice "file index: indexing…" (every attempt while not ready); ready → `Completion.Complete(prefix, index.Match(prefix, 20))`, replacement = `"@" + replacement`; candidates notice = "paths: " + up to 20 joined, with `… (+N more)` beyond. Nothing else changes; Tab anywhere else still does nothing.

## Budget (pinned)

`PathIndexBudgetTests` (`Category=Perf`): creates a generated 20,000-file tree in the scratch temp dir (small files, nested dirs, a `.gitignore` excluding part of it), measures index build (assert ≤ **5 s**) and a warm query (assert ≤ **50 ms**), prints both, cleans up. Main test passes exclude `Category=Perf`; `verify.sh`/`verify.ps1` add a "path index budget" step running exactly that trait. Startup budget: unchanged — the index is never built before first use (asserted by construction + the existing startup step).

## Deviations

1. `FileIndex.Match(prefix, max)` treats `max <= 0` as "no cap" (the parameter otherwise caps as
   pinned). The session requests all matches, because completing the longest common prefix over a
   truncated first-20 set can insert text not shared by all matches; the notice still lists up to
   20 candidates.
2. A directory symlink is neither indexed nor descended: the pinned "not followed" is read as
   "absent from the index" rather than "listed but empty". File symlinks are indexed as files.
3. Zero matches for an `@token` shows the dim notice `paths: no matches`; the spec's no-progress
   branch would otherwise render as an empty candidate list.
4. Completing an `@token` whose cursor sits before trailing text keeps that trailing text (the
   replacement changes only the token; the cursor lands at the input's end, as after slash
   completion).

## Seams

- Character-class corner cases and `\`-escapes in `.gitignore` are best-effort; specifically `[^…]` negates here while git treats `^` literally, and non-canonical `**` positions (e.g. `a**/b`) over-match. Not exhaustively git-compatible.
- The index is rooted at the session's working directory: candidates are working-directory-relative and `.gitignore` files above it are not loaded — launch at the repo root for the full git chain. (Rooting at the repo root instead would emit paths the session's tools cannot resolve when launched from a subdirectory.)
- The warm-query budget guards pathological implementations (e.g. O(n²)); the build budget over a real 20k-file walk is the meaningful assertion.
- Completion inside quoted strings or mid-token edits beyond the cursor are out of scope (token must END at the cursor).
