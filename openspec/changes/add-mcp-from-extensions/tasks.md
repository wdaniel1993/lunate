# Tasks: MCP servers from extensions (T-41)

## 1. Contract (Extensibility.Abstractions)

- [x] 1.1 `McpServerConfig`, `McpServerRegistration`, `ToolExposure` (STJ round-trip tests)
- [x] 1.2 `IExtensionContext.RegisterMcpServer`; PublicAPI.Unshipped

## 2. Loader registry (Extensibility)

- [x] 2.1 `McpServerRegistry` collected at creation time; exposed on the loader; duplicate names refused + logged naming both ids
- [x] 2.2 Registration-only test (nothing starts, nothing spawns)

## 3. Protocols wiring

- [x] 3.1 `McpServerOptions` gains exposure (default Direct), overrides and namespace name (default server name)
- [x] 3.2 `McpToolAdapter` carries resolved exposure + namespace; unknown override logged
- [x] 3.3 Regression: existing MCP suites green

## 4. Coding mapping + end to end

- [x] 4.1 `ExtensionMcpServers` mapping (registration → options/host, lazy)
- [x] 4.2 Fixture extension registering the real `TestMcpServer`; e2e test: namespaced, `Hidden` gated out of Declarations, one call round-trips

## 5. Gate

- [ ] 5.1 `bash scripts/verify.sh` green; deviations recorded in design.md
