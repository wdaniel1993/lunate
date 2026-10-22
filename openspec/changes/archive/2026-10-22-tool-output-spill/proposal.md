# Spill truncated tool output to a file the model can read

## Why

`ToolOutput.Truncate` keeps the first and last half of a large tool result and drops the middle with a marker. For a long build log, a large diff or a `bash` command that dumped thousands of lines, the dropped middle is often the part that matters — and the model cannot ask for it.

## What changes

When a tool result exceeds the configured limit, the **full** output is written to a file in the session's directory and the marker names that path, so the model can `read` it (with offset/limit) like any other file. The in-band output keeps its current shape (first half, marker, last half) with the path appended to the marker.

- The file lives under the session's own directory (`<sessions-dir>/<session-id>/tool-output/`), so it dies with the session and is never written for `--no-session` runs.
- No new config. The existing `output.toolResultLimit` keeps deciding when to spill.
- A failed write never fails the tool call: the marker falls back to the current text and says the spill failed.

## Impact

- Change spec: `agent-tools` (the truncation requirement).
- Code: `ToolOutput`, `AgentHarness`'s tool-result path, the session store.
- Sessions stay schema 3 — the spill files are auxiliary, not transcript state.
