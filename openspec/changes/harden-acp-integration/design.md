# Design: Harden the ACP integration (T-28 follow-ups)

## Structure

- `src/Lunate.Agent/ITextFileAccess.cs`: gains `(string Text, long Length)? ReadPrefix(string path, int maxBytes)` — a bounded prefix plus the file's total length when the provider can report both; `null` when the provider cannot probe.
- `src/Lunate.Coding/LocalTextFileAccess.cs`: implements the probe — reads up to `maxBytes` bytes, decodes UTF-8 (BOM stripped), returns `(decoded, FileInfo.Length)`.
- `src/Lunate.Coding/ReadTool.cs`: probe-first flow (below).
- `src/Lunate.Protocols/Acp/ClientTextFileAccess.cs`: `ReadPrefix` → `null`; bounded round trips; error taxonomy.
- `src/Lunate.Protocols/Acp/ClientApprover.cs`: bounded permission requests.
- `docs/guide.md`: the `IAcpServer` sketch (line ~528) refreshes to `Func<AcpSessionContext, AgentHarness>`.

## Pins

- **ReadTool flow**: `Exists` → probe. Probe non-null and contains `'\0'` → binary error naming the probe's `Length` (exact, the old message format). Probe non-null and clean → read the full text and return it **without re-scanning** (pre-T-28 semantics; the spec's window is the contract). Probe `null` (client-backed) → read the full text and refuse on `'\0'` anywhere, naming the decoded byte count (plus 3 for a BOM) as today.
- **Why the client cannot probe**: the ACP read is line-addressed — a line-limited read is byte-unbounded (a newline-less binary would still be read whole), and the client owns the file anyway. Documented; the client path stays stricter (whole-text check).
- **Timeouts**: `ClientTextFileAccess` takes `TimeSpan timeout = 30 s` (constructor, injectable for tests); the read/write bridge wraps the task with `WaitAsync(timeout)`; a `TimeoutException` maps to `IOException` naming the wait (culture-invariant). `ClientApprover` takes `TimeSpan permissionTimeout = 10 min`; expiry logs and declines. `LibAcpServer` uses the defaults (no settings surface).
- **Error taxonomy**: `ClientTextFileAccess` maps the client's error (`Acp.JsonRpc.RequestErrorException`) to `FileNotFoundException` when its code is `-32002` (ResourceNotFound) and to `IOException` for any other non-zero code — when a code is set it alone decides, so a stray "not found" in another error's text cannot mask it. The message fallback ("not found" / "ENOENT" / "no such file", ordinal ignore-case) applies only when no code is set (`0`, LibAcp's unset default). `Exists` returns `false` only for the mapped not-found and rethrows everything else. Read/write surface the mapped exception; the file tools' catches turn them into "could not be read/written" messages.
- **Spec resolution**: `agent-files`' read requirement states both provider behaviours (local: first 8,192 bytes; client-backed: the received text) — the requirement previously said "first 8,192 bytes" only, which the T-28 seam change had silently outgrown.

## Tests (pinned)

- Local probe: a small binary still errors with its exact size (existing test stays green); a larger file (NUL early, > 8,192 bytes) errors with the exact total size; a NUL beyond the first 8,192 bytes reads as text (the window boundary, spec-faithful); `LocalTextFileAccess.ReadPrefix` unit: prefix text + exact length, small and large files, empty file.
- Client-backed: a served buffer containing a NUL refuses as binary (whole-text path); a missing-file client error surfaces as "file not found"; another client error surfaces as "could not be read"; a non-not-found code whose message says "not found" still surfaces as "could not be read" (the code decides); a code-less error falls back to the message; `Exists` returns false only for not-found and rethrows the rest (assert via a read); the edit and write tools surface rethrown read/write failures as "could not be read" / "could not be written" (seam tests).
- Timeouts: a client that ignores a file request → the tool fails as an I/O error after a short injected timeout (no hang); a client that ignores a permission request → the call declines after a short injected timeout, run continues.
- Guide: markdownlint green.

## Deviations

- Constructor defaults are expressed as `TimeSpan? timeout = null` (and `TimeSpan? permissionTimeout = null`) plus `internal const int DefaultTimeoutSeconds = 30` / `DefaultPermissionTimeoutSeconds = 600`: C# optional parameters must be compile-time constants, so a `TimeSpan` default cannot be written directly. The observable defaults are the pinned ones.
- The rethrown non-not-found `Exists` failures surface at the tool boundary across all three file tools: the read tool's `Exists` call sits inside its existing `IOException`/`UnauthorizedAccessException` catch; the edit tool wraps its `Exists` call and its `WriteRaw` call the same way ("could not be read" / "could not be written"); the write tool gains matching read-side and write-side catches (it had none). `Exists` → probe/read/write order, happy paths and genuine-not-found results are unchanged.
- LibAcp's `RequestErrorException.Code` is a non-nullable `int`; `0` (the unset default, never emitted by LibAcp's factories) is treated as "no code", so only there does the not-found fallback consult the message.
- LibAcp's error shape was verified against the pinned commit (4bd32b3): an error response surfaces as `Acp.JsonRpc.RequestErrorException` with `Code` and `Message`; `ResourceNotFound` is code `-32002`, message `"Resource not found: {path}"`. No adaptation needed.
- The permission-timeout tests run the harness directly over the raw connection (a full ACP session uses the pinned 10 min default; the decline-continues-the-run behaviour is asserted at the harness level instead).

## Seams

- The not-found classification is message-based only for a code-less client error (`0`; ACP defines no error taxonomy for the file system) — a client with unusual wording falls back to "could not be read", acceptable and documented. A code-carrying error is classified by the code alone, so one failure class can never be masked as another.
- The local probe reads the file twice (probe + full read) — the pre-T-28 behaviour, negligible on local disk.
