# Tasks: sample-mcp-from-extension

## 1. Core: /mcp merges both server sets

- [ ] 1.1 `ExtensionMcpServers` gains an internal `Statuses` passthrough over the set's statuses
- [ ] 1.2 `McpStatusText()` merges configured + extension-registered statuses (same segment shape/join); "no servers configured" only when both sets are empty (extension host absent included)
- [ ] 1.3 `Lunate.Coding.Tests`: interactive test — temp extension store with a test extension registering an MCP server (fake server dll) → `/mcp` lists it with `started, N tools`; no configured servers → NOT "no servers configured". Precedent: `ExtensionCompositionEndToEndTests` store pattern

## 2. The sample extension

- [ ] 2.1 `samples/extensions/mcp-from-extension/McpFromExtensionExtension/`: manifest (id `mcp-from-extension`, apiVersion, hooks/services none), factory registering `McpServerRegistration("fake", config, Direct, {"secret": Hidden})` — config `dotnet <FakeMcpServer.dll>` with `Environment = {"LUNATE_FAKE_MCP_MARKER": <settings markerPath>}`; settings schema (`markerPath`); README with the capability table
- [ ] 2.2 `FakeMcpServer` console project (official `ModelContextProtocol` 2.2.0, already in-repo): tools `echo` (text), `add` (structured `{sum}`), `secret`; tools added before `McpServer.Create`; marker file (`started pid=<n>`, call lines) via the env var; stderr for the rest

## 3. The sample tests (the done-when)

- [ ] 3.1 Registration collected: `host.Runner.McpServers.List()` shows `fake` with the right command, exposure and the `secret` override
- [ ] 3.2 Production composition: build `ExtensionMcpServers` over the registrations exactly as `ExtensionSession` does (IVT `McpFromExtensionExtension.Tests` in `Lunate.Coding.csproj`); list once → tools `fake__echo` / `fake__add` / `fake__secret` with exposures `Direct`/`Direct`/`Hidden`; a second listing does not restart (marker count stays one)
- [ ] 3.3 Scripted run: `fake__echo` declared to the model (first request contains it), call round-trips through approval (risk `Execute`) with the echoed output; `fake__secret` in neither the declaration nor any callable path; `fake__add` structured result asserted on the tool result
- [ ] 3.4 Failure isolation: a test-local registration pointing at a missing binary → logged naming server + extension, session continues without its tools
- [ ] 3.5 Suite serialized from the start (`Parallelization(Mode = None)`) — the lsp sample's CI lesson; budgets under the runner's per-handler timeout
- [ ] 3.6 Layering allowlist gains the sample's edges; both projects join `lunate.sln`

## 4. Gate + record

- [ ] 4.1 `bash scripts/verify.sh` green (both cultures); deviations recorded in design.md
- [ ] 4.2 Archive, PR, CI green
