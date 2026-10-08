# Tasks

## 1. TestMcpServer fixture (TDD, red-first)

- [x] 1.1 `tests/tools/TestMcpServer` console app on the official SDK (stdio; echo/add/slow/boom/spawn_tool; start marker via `LUNATE_TEST_MCP_MARKER`); added to the solution; layering registration if needed
- [x] 1.2 Prove it works standalone (manual probe: initialize + tools/list round-trip over stdio)

## 2. Client (TDD, red-first)

- [x] 2.1 `ModelContextProtocol` package in `Lunate.Protocols`; `McpServerOptions` + `McpServerHost` (lazy start, serialized start, list, list-changed callback, dispose/stops process; PublicAPI entries)
- [x] 2.2 `McpToolAdapter : ITool` (prefix, schema, risk Execute, call mapping, structured content, error/timeout/cancel translation per design)
- [x] 2.3 Tests per design (lazy marker, listing, call round-trips, cancel, timeout, crash, spawn_tool refresh, server death, dispose, both cultures)

## 3. Close

- [x] 3.1 `dotnet csharpier format .`; `bash scripts/verify.sh` green (incl. de-AT; startup budget untouched); `openspec validate add-mcp-client --type change --strict`; self-review; commit per group
