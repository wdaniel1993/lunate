# Design: add-mcp-client

## Context

Guide MCP section (lines 514-530) + tool-model notes (246) are binding; the card row says "lazy start, tool wrapping, cancel, list changes; tests against TestMcpServer". T-30 owns `mcp.json` parsing, approval wiring and TUI; T-41/T-46 bring `RegisterMcpServer` for extensions. No new ADR (uses the suggested SDK per the tech table).

## Package

`ModelContextProtocol` (official MCP C# SDK), latest stable at apply time; used by both `Lunate.Protocols` and `tests/tools/TestMcpServer`. Record the exact version in the PR. If only a prerelease supports .NET 10, use it and flag.

## `McpServerHost` (pinned behavior)

```csharp
public sealed class McpServerHost : IAsyncDisposable
{
    public McpServerHost(McpServerOptions options, Action<IReadOnlyList<ITool>>? onToolsChanged = null);
    public string Name { get; }                       // server id, also the tool prefix
    public ValueTask<IReadOnlyList<ITool>> GetToolsAsync(CancellationToken ct); // lazy start
    public bool Started { get; }                      // observability; no process before first use
}
public sealed record McpServerOptions(string Name, string Command, IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string>? Environment = null, string? WorkingDirectory = null,
    TimeSpan? CallTimeout = null);                    // default 60 s
```

- **Lazy start**: construction spawns nothing (proven by the marker file in TestMcpServer); the first `GetToolsAsync` starts the process, initializes, lists tools. Re-entrant/serialized via an internal gate; start failure → clear exception/message naming the server.
- **Naming**: tools exposed as `` `server__tool` `` (double underscore); description = MCP description (fallback: none); schema = the tool's JSON schema.
- **Risk**: each wrapped tool carries risk `Execute` (guide) so the default approval policy asks. Annotations mapped when the MCP tool provides them; `Execute` stays the override in v1.
- **List changes**: the SDK's list-changed notification (confirm the exact SDK surface during apply) triggers a re-list; `onToolsChanged` receives the new wrapped set; wrapped `ITool` identity: adapters are stable per tool name where possible (so the registry/caches can reconcile).
- **Dispose/stop**: idempotent; closes the SDK client/transport; kills the child if it lingers; no orphan processes (test asserts the process is gone).

## `McpToolAdapter : ITool`

- `Name` = `server__tool`; `Description`, `ParametersSchema` from the MCP listing; `Annotations` from MCP annotations when present.
- `ExecuteAsync`: map args JSON → `CallToolAsync`; success → `ToolResult` whose text is the concatenated text content (bounded per the loop's rule; JSON/structured content into `StructuredContent` when the MCP result carries it); MCP `isError` results → error `ToolResult` with the server's message; transport/protocol failures → error `ToolResult` naming the server and next step; **timeout** (CallTimeout) → error `ToolResult` "timed out after Xs"; **cancellation** (`ct` cancelled) → rethrow `OperationCanceledException` (the loop's cancellation path); nothing else escapes.
- Server process death mid-session → subsequent calls are error results ("server exited"), Lunate stays up (test).

## `tests/tools/TestMcpServer`

- Console app, official SDK server side, stdio transport; tools: `echo` (returns its input), `add` (two ints, structured result), `slow` (sleeps up to N seconds honoring cancellation), `boom` (throws), `spawn_tool` (registers `late_tool` and sends the list-changed notification).
- Start marker: appends a line to the file named by env `LUNATE_TEST_MCP_MARKER` at startup (tests prove lazy start + count restarts).
- Added to `lunate.sln` so CI builds it; LayeringChecker registered (no Lunate references — a leaf).
- Tests spawn it via the same repo-root-relative path pattern used elsewhere (dotnet exec of the built dll).

## Tests (Lunate.Protocols.Tests, replacing the placeholder)

No process before first use (marker absent); list → `server__echo`, `server__add`, `server__slow`, `server__boom`, `server__spawn_tool` with schemas + risk `Execute`; `add` round-trip incl. structured content; `slow` cancelled mid-call → `OperationCanceledException` and the server observes cancellation; `slow` with a 200 ms CallTimeout → timeout error result; `boom` → error result, client still usable; `spawn_tool` → onToolsChanged fires with `late_tool` present; server killed (kill the process externally) → error result, no crash; dispose → process gone; double dispose safe; both cultures; startup budget untouched (no MCP at startup — the existing budget gate covers it).

## Out of scope

`mcp.json` + approvals + TUI (T-30), extension `RegisterMcpServer` (T-41), HTTP transports, resources/prompts/sampling, session persistence.
