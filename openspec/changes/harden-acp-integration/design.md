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
- **Error taxonomy**: `ClientTextFileAccess` maps the client's error (`Acp.JsonRpc.RequestErrorException`) to `FileNotFoundException` when it indicates a missing file (message contains "not found" / "ENOENT" / "no such file", ordinal ignore-case; the code is consulted when the client sets one) and to `IOException` otherwise. `Exists` returns `false` only for the mapped not-found and rethrows everything else. Read/write surface the mapped exception (the tools' existing catches turn them into "could not be read/written" messages).
- **Spec resolution**: `agent-files`' read requirement states both provider behaviours (local: first 8,192 bytes; client-backed: the received text) — the requirement previously said "first 8,192 bytes" only, which the T-28 seam change had silently outgrown.

## Tests (pinned)

- Local probe: a small binary still errors with its exact size (existing test stays green); a larger file (NUL early, > 8,192 bytes) errors with the exact total size; a NUL beyond the first 8,192 bytes reads as text (the window boundary, spec-faithful); `LocalTextFileAccess.ReadPrefix` unit: prefix text + exact length, small and large files, empty file.
- Client-backed: a served buffer containing a NUL refuses as binary (whole-text path); a missing-file client error surfaces as "file not found"; another client error surfaces as "could not be read"; `Exists` returns false only for not-found and rethrows the rest (assert via a read).
- Timeouts: a client that ignores a file request → the tool fails as an I/O error after a short injected timeout (no hang); a client that ignores a permission request → the call declines after a short injected timeout, run continues.
- Guide: markdownlint green.

## Deviations

(filled during apply; none yet)

## Seams

- The not-found classification is message-based where the client sets no code (ACP defines no error taxonomy for the file system); a client with unusual wording falls back to "could not be read" — acceptable, documented.
- The local probe reads the file twice (probe + full read) — the pre-T-28 behaviour, negligible on local disk.
