# Tasks — tool output spill

## 1. Spill writer

- [x] 1.1 Add red tests: a result over the limit writes a file whose bytes equal the full output; a result at or under the limit writes nothing; the file path is inside the session's own directory; a resumed session can read the path from the marker.
- [x] 1.2 Add the spill writer: per-session `tool-output/` directory, deterministic `<run>-<seq>.txt` name, flush then atomic move, 64 MB ceiling.
- [x] 1.3 Commit: `feat(agent): spill a truncated tool result to a session file`.

## 2. Marker and failure path

- [x] 2.1 Add red tests: the marker names the absolute path and the omitted count culture-invariantly; a failed write keeps today's marker and reports the failure; `--no-session` writes nothing and keeps today's marker; over the ceiling the marker says so.
- [x] 2.2 Extend `ToolOutput.Truncate` (or its caller) with the optional path; never fail the tool call on a spill failure.
- [x] 2.3 Commit: `feat(agent): name the spilled output in the truncation marker`.

## 3. Verification

- [x] 3.1 Build, csharpier, the Agent and Coding suites, `openspec validate tool-output-spill --strict`.
- [x] 3.2 Update `docs/guide.md` where it describes tool-output truncation.
