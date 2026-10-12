# Tasks: sample-mcp-from-extension

## 1. Core: sources, collisions, crash, /mcp merge

- [x] 1.1 Per-server source: `McpServerOptions` gains `Source` (set by `McpConfig.Load` to its file path); `ExtensionMcpServers` sets `extension '{id}'`; `McpServerStatus` gains `Source`
- [x] 1.2 Collision filter (config wins), shared by interactive/print/ACP: registrations whose name matches a configured server are skipped before listing; the collision is logged naming both sources; interactive compose order adjusted so both sets are known before the extension listing
- [x] 1.3 Crash handling: a started server that has exited flips to `Failed` ("the server process exited") when next observed — `/mcp` liveness probe + after a failed tool call — with one notice per server (deduped on the transition); tools read unavailable
- [x] 1.4 `ExtensionMcpServers.Statuses` passthrough (source included); `McpStatusText()` merges both sets with sources (`name — started, N tools (source)`; `shadowed by {source}` for collisions; "no servers configured" only when both empty); never prints env values or headers

## 2. Kit: public MCP composition (maintainer steer — no sample IVT)

- [x] 2.1 `Lunate.Extensibility.Testing` references `Lunate.Coding` (+ allowlist edge `Testing → Coding`); `Lunate.Coding.csproj` gains `InternalsVisibleTo("Lunate.Extensibility.Testing")`
- [x] 2.2 `ExtensionTestHost.ComposeMcpServersAsync(ct)`: production mapping (`ExtensionMcpServers`) over `Runner.McpServers.List()`, list once, merge into `Tools`, return public statuses (name, state, toolCount, reason, source) + public state enum; the host owns the set (disposed on `StopAsync`/`DisposeAsync`)
- [x] 2.3 The kit's own `McpFromExtensionEndToEndTests` moves onto the public API (behavior preserved)

## 3. The sample extension

- [x] 3.1 `samples/extensions/mcp-from-extension/McpFromExtensionExtension/`: manifest (id `mcp-from-extension`, apiVersion), factory registering `fake` (Direct; `{"secret": Hidden}`; env marker from settings), settings schema (`markerPath`), README capability table
- [x] 3.2 `FakeMcpServer` console project (official `ModelContextProtocol`, in-repo): `echo`, `add` (structured), `secret`, `crash`; tools added before `McpServer.Create`; marker file via the env var; stderr for the rest

## 4. The sample tests (the done-when)

- [x] 4.1 Registration collected (name/command/exposure/override); nothing started at registration (no marker)
- [x] 4.2 `ComposeMcpServersAsync`: names `fake__echo`/`fake__add`/`fake__secret`/`fake__crash` with exposures `Direct`/`Direct`/`Hidden`/`Direct`; one start across listings (marker count)
- [x] 4.3 Scripted run: `fake__echo` declared + call through approval (risk `Execute`) with output; `fake__add` structured result asserted; `fake__secret` neither declared nor callable via a nested-execution probe
- [x] 4.4 Failure isolation: test-local registration with a missing binary → logged (server + extension), session continues
- [x] 4.5 Unload/stop stops the server: PID gone after `StopAsync`/`DisposeAsync`
- [x] 4.6 Crash: `fake__crash` call errors; server reads failed ("the server process exited") with one notice; tools unavailable
- [x] 4.7 Suite serialized (`Parallelization(Mode = None)`); budgets under the runner's per-handler timeout

## 5. Interactive tests (`Lunate.Coding.Tests`)

- [x] 5.1 Extension store + test extension registering the fake → `/mcp` lists it with status + `extension '{id}'` source (no configured servers → not "no servers configured")
- [x] 5.2 Collision: configured + colliding registration → shown shadowed, configured wins, no duplicate-name throw
- [x] 5.3 Sentinel env values in both sets → absent from the `/mcp` notice
- [x] 5.4 Crashed server → `/mcp` shows failed + notice fired

## 6. Gate + record

- [ ] 6.1 Both sample projects join `lunate.sln`; `bash scripts/verify.sh` green (both cultures); deviations in design.md
- [ ] 6.2 Archive, PR, CI green
