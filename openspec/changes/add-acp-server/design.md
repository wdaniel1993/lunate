# Design: ACP server (T-27)

## Structure

- `src/Lunate.Protocols/Acp/IAcpServer.cs` (new, public): the guide's interface —
  `Task RunAsync(Stream input, Stream output, Func<AgentHarness> createHarness, CancellationToken ct)`.
- `src/Lunate.Protocols/Acp/LibAcpServer.cs` (new, internal): the adapter implementing it on LibAcp.
  Internal behind `IAcpServer` so a later SDK switch stays cheap (ADR-0020); `Lunate.Protocols`
  grants `InternalsVisibleTo` to `Lunate.Protocols.Tests` and `lunate` (Lunate.Coding), and only
  `IAcpServer` appears in `PublicAPI.Unshipped.txt`.
- `src/Lunate.Protocols/Acp/AcpEventMapper.cs` (new): `AgentEvent` → `session/update` payloads (pure, golden-testable).
- `src/Lunate.Coding/Cli.cs`: the `--acp` mode wiring (stdio streams, nothing else on stdout).
- Tests: `tests/Lunate.Protocols.Tests/` — in-process client over a pipe pair (LibAcp `ClientSideConnection`), mapper goldens; a Coding-level stdout-purity test for the real `--acp` entry.

## Protocol behaviour (pinned)

- **initialize**: protocol version `1`; agent capabilities limited to what we implement (prompt; no fs/terminal yet — T-28 announces those); client-requested higher versions negotiate down; unsupported client capabilities are logged, not fatal.
- **session/new**: `createHarness()` builds a fresh `AgentHarness` per ACP session; the session's cwd maps to the harness workspace; the ACP session id ↔ Lunate session pairing is kept in the adapter (one map, no persistence in v1).
- **session/prompt**: one `AgentHarness.RunAsync` run; user content blocks → prompt text (text blocks only in v1; other blocks logged and skipped); `AgentEvent`s stream as `session/update` notifications via the mapper; the prompt response carries the stop reason: `end_turn` for `RunFinished`, `cancelled` for cancel, `refusal`/error mapping for `RunError` (exact mapping pinned by mapper tests).
- **session/cancel**: cancels the in-flight run's `CancellationToken` (Esc semantics); a cancelled prompt responds with the cancelled stop reason; no stray updates after the response.
- **Update mapping** (guide table, names checked against LibAcp's stable v1 types at apply time): `TextMessageStart/Content/End` → agent message chunks (ordered); `ToolCallStart/Args/End` → tool call; `ToolCallResult` → tool call update; `RunStarted/RunFinished/RunError` → the prompt response; `UsageUpdated`/`Retrying`/`CompactionApplied`/`StepLimitReached` → not sent, logged.
- **stdout discipline**: only protocol frames on stdout; every log line goes to stderr; the TUI and print mode never start in this mode.

## CLI (pinned)

`lunate --acp` selects the ACP server; combined with `-p` or a prompt it is an error (usage message, exit 2); stdin/stdout are the protocol streams; `Esc`-style cancellation is driven by the client's `session/cancel`.

## Tests (pinned)

- In-process pipe pair: our `LibAcpServer` on one end, LibAcp's client connection in the test on the other. Cases: initialize handshake (version + capabilities), session/new, prompt streaming (ordered chunks, `end_turn`), cancel mid-run (cancelled stop reason, no updates after the response), unknown method → JSON-RPC error, prompt with a non-text block (logged, skipped).
- Mapper goldens: every mapped `AgentEvent` shape → the exact wire payload (byte-exact JSON where practical).
- stdout purity: spawn the built `lunate` binary with `--acp`, feed a minimal session, assert stdout parses as pure JSON-RPC frames while stderr carries logs (skipped where the published binary is unavailable, mirroring the existing publish-dependent tests).
- No platform-dependent literals; ordinal/Invariant everywhere.

## Manual (post-merge, noted on the card)

One real Zed session before Phase 5 closes (guide §Tests) — checklist on the kanban card; also the natural first smoke of the adapter against a real client.

## Deviations

1. **The harness factory carries the session cwd.** The pinned interface took a parameterless
   `Func<AgentHarness>`, but session/new also pins "the session cwd as the harness workspace", and
   a parameterless factory cannot carry the cwd. `IAcpServer.RunAsync` (and `LibAcpServer`) take
   `Func<string, AgentHarness>` — the parameter is the ACP session cwd — so the cwd-to-workspace
   mapping is implementable and testable. API shape only; no behaviour change.
2. **ACP mode lives in `AcpMode.cs`.** `Cli.cs` keeps the dispatch, the mutual-exclusion error and
   the help text; the stdio wiring, stderr logging and the production harness factory sit in
   `src/Lunate.Coding/AcpMode.cs` so `Cli.cs` stays small.
3. **LibAcp API shape (verified against the package and its source).** `SessionId`/`ToolCallId`
   are record structs and the optional agent methods default to `MethodNotFound`. `IAgent.AuthenticateAsync`
   is required, not optional: the adapter implements it by throwing `MethodNotFound` and advertises
   no `AuthMethods`. LibAcp dispatches every inbound message on
   its own task and serializes writes per connection, so `session/cancel` reaches the adapter
   while a prompt is in flight — the pinned cancel behaviour holds. No blocking deviation.
4. **`LibAcpServer` is internal, behind `IAcpServer`.** The adapter was initially public (with a
   `PublicAPI.Unshipped.txt` entry), which would freeze the concrete LibAcp surface as public API
   and undermine the seam's "a later SDK switch stays cheap" point (ADR-0020). It is now internal:
   `Lunate.Coding` constructs it through `InternalsVisibleTo` and types the server as `IAcpServer`,
   and only the interface is published. Visibility only; no behaviour change.

## Seams

- `session/load`, `resume`, `list`, `close`, `set_mode`, `set_config_option`: out of v1 scope (guide: initialize, new session, prompt, cancel); LibAcp supports them, the adapter does not surface them yet.
- Permissions and the editor file system: T-28.
- `ResourceLinkContent` blocks are mandatory in ACP v1 prompts; v1 logs and skips them — mapping
  (for example to a `@path` reference the model can read) is carried into T-28 scope.
- The factory's `string` parameter widens to a request-shaped value when T-28 adds additional
  directories and MCP servers.
- Multi-session concurrency: v1 handles sessions sequentially per connection (one active run); revisit if an editor needs parallel sessions.
