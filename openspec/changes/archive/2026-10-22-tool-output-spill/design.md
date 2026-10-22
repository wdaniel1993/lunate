# Design — tool output spill

## Where the file goes

`<sessions-dir>/<session-id>/tool-output/<run>-<seq>.txt` — beside the session file, under the same per-session directory the image blobs use. Consequences:

- The lifetime is the session's. Deleting a session deletes its spill files; nothing global accumulates.
- `--no-session` runs write no file: the model keeps the truncated form, which is today's behaviour.
- A resumed session can read an old spill file only if its path is in the transcript — it is, in the marker.

File naming is deterministic per run and call sequence (`<run>-<seq>.txt`), so a retried call overwrites its own file rather than leaking a second one.

## The marker

Today: `… [N characters omitted] …`. With a spill: `… [N characters omitted; full output at <path>] …`. The path is absolute (the model's tools take absolute paths without ambiguity) and is written only after a successful flush.

## Failure behaviour

A spill write is best-effort. If the directory cannot be created or the write fails, the tool result keeps today's marker unchanged and the failure is reported as a notice in interactive sessions and on stderr once in print mode — never as a tool error, because the tool itself succeeded.

## Bounds

Spill writes are capped at a generous ceiling (64 MB) so a runaway command cannot fill the disk through one call; above it the marker says the output was too large to spill. The ceiling is a constant, not config, until someone needs otherwise.

## Alternatives considered

*Ask the user to configure a bigger limit* — hides the problem. *Stream the output to the file while the tool runs* — changes the tool pipeline and misses the case where a tool returns one huge string. *Keep the middle instead of the edges* — arbitrary, and still bounded. Rejected.

## Tests

- a result over the limit writes a file whose content equals the full output and whose marker names the path;
- a result under the limit writes nothing;
- `--no-session` writes nothing and keeps today's marker;
- a failed write keeps today's marker and reports the failure;
- the marker is culture-invariant in its count;
- resuming a session and reading the spill path returns the full content.
