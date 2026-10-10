# Design: ACP permissions, editor file system and resource links (T-28)

## Structure

- `src/Lunate.Protocols/Acp/AcpSessionContext.cs` (new): `string Cwd`, `IToolApprover? ClientApprover`, `ITextFileAccess? FileAccess` — built by the adapter from the live client connection; consumed by the Coding-side factory.
- `src/Lunate.Protocols/Acp/ClientApprover.cs` (new): raw client prompt — builds the `session/request_permission` request (tool call info from `ITool.Name` + args summary), maps `allow_once`/`allow_always`/`reject_once`/`reject_always` to allow/decline, remembers "always" outcomes per tool name for the connection's session; a cancel or a request error resolves as a decline.
- `src/Lunate.Protocols/Acp/ClientTextFileAccess.cs` (new): `ITextFileAccess` over `fs/read_text_file`/`fs/write_text_file` (only constructed when the client advertised the capability at initialize).
- `src/Lunate.Coding/ITextFileAccess.cs` (new): the tool-facing seam — `bool Exists(path)`, `string ReadAllText(path)`, `(string Text, bool HasBom) ReadRaw(path)`, `void WriteAllText(path, content)`, `void WriteRaw(path, text, hasBom)`; `LocalTextFileAccess` (the existing `File`/`TextFile` calls, moved behind the interface) and tools take it as an optional constructor parameter (default local).
- `src/Lunate.Coding/AcpApprover.cs` (new): policy-first composition — `NonInteractiveApprover.IsAllowed(policy, risk)` already allows → true without prompting; otherwise delegate to the client approver.
- `src/Lunate.Coding/AcpMode.cs`: builds the widened factory; wires the approver + file access into harness options and tool registry.
- `LibAcpServer.cs`: builds `AcpSessionContext` per session (cwd from `NewSessionRequest`, approver + file access when the client supports them), calls the factory with it; maps `ResourceLinkContent` in `PromptText`.
- Tests: `tests/Lunate.Protocols.Tests/` (permission round trips, fs routing, resource links, cancel-during-request) and `tests/Lunate.Coding.Tests/` (policy composition, tool seam defaults).

## Pins

- **Approval flow**: policy allows (`Ask` → ReadOnly; `AutoEdit` → ReadOnly + Write) → run without prompting. Else `session/request_permission` with options allow once / allow always / reject. Outcomes: allow_once → execute; allow_always → execute + remember per tool name (session-scoped, cleared with the session); reject_once → decline (tool error result, as `IToolApprover=false` does today); reject_always → decline + remember. Cancel while the request is in flight → decline. Request error → decline (safe default). `Execute`-risk (commands) prompts under BOTH policies. No `--yolo` in ACP mode (CLI conflicts unchanged).
- **File access routing**: when the client advertised `fs` at initialize, `ReadTool`/`WriteTool`/`EditTool` receive `ClientTextFileAccess`; reads see the editor's buffer; writes go to the client. BOM: `ReadRaw`/`WriteRaw` through the client treat BOM as absent (the client owns the file; documented). Other file consumers (AGENTS.md, settings, history) stay local — they are session concerns, not workspace content.
- **Resource links**: `ResourceLinkContent` blocks convert to text in prompt order: a `file://` URI that resolves inside the workspace → `@<relative path>`; anything else → `<name> (<uri>)`. Conversion happens in the adapter's `PromptText`; the guide's "text content blocks" sentence in the T-27 spec is updated to include resource links; other block kinds stay logged-and-skipped.
- **Factory context**: `IAcpServer.RunAsync(input, output, createHarness, ct)` keeps its shape; the factory becomes `Func<AcpSessionContext, AgentHarness>`. The adapter always builds the approver; file access only with the capability. `additionalDirectories`/`mcpServers` from `session/new` are logged and ignored (seam).
- **Capabilities**: the agent still advertises prompt-only; nothing new. The client's fs capability is read from initialize and drives the routing.

## Tests (pinned)

- Pipe pair, permission round trip: `Ask` + write tool → request arrives (tool call info sane) → allow_once executes; reject_once → declined result; allow_always → second call runs without a request; reject_always → second call auto-declined; `AutoEdit` + write → no request; `Execute` → requests under both policies; cancel during the request → decline, no hang.
- Pipe pair, fs routing: fake client serves a modified buffer for `fs/read_text_file` → the read tool returns the buffer; a write tool call → client receives `fs/write_text_file` and the local file is untouched; without the capability → local disk used (assert both).
- Pipe pair, resource links: file URI inside the workspace → `@relpath` appears in the model-visible prompt (captured via the fake harness); outside/other URI → `name (uri)`; mixed prompt order preserved.
- Composition tests (Coding): policy-first approver matrix (allowed → no client call; else delegate), tool seam default = local.
- No platform-dependent literals; ordinal/Invariant.

## Manual (post-merge, card)

One real Zed session: approvals round trip in the editor dialog (allow once / always / reject), a file read reflecting an unsaved buffer, before Phase 5 closes.

## Deviations

(filled during apply; none yet)

## Seams

- BOM preservation through the client is the client's concern (documented above).
- `additionalDirectories` / `mcpServers` from `session/new`: logged, ignored (future card if needed).
- "Always" memory is per connection/session and per tool name; finer scoping (per argument pattern) is future work if an editor needs it.
