# Proposal: sample-mcp-from-extension

## Why

Guide card T-46 (phase 6): the fitness suite proves `RegisterMcpServer` — a reference extension contributing an MCP server, with its tools namespaced and exposure-gated, visible in the TUI. Building the sample exposed a real gap: `/mcp` lists only the **configured** servers (the `mcp.json` set) — an extension-registered server's tools are merged into the session and callable, but its status never reaches the `/mcp` surface (with no configured servers, `/mcp` even reads "no servers configured"). The sample's done-when ("namespaced MCP tools visible in the TUI") cannot hold without closing that.

## What

- **The sample** (`samples/extensions/mcp-from-extension/`): an extension that registers one MCP server in its factory — `McpServerRegistration` with the `mcp.json` shape, a start-marker via the config's environment, exposure `Direct` and a per-tool override (`secret` → `Hidden`). Registration starts nothing; the first listing starts the server exactly once (the existing lazy contract).
- **A fake MCP server** (`FakeMcpServer`, console project in the sample): built on the official `ModelContextProtocol` SDK already used by `tests/tools/TestMcpServer` (no new package) — `echo` (text), `add` (structured), `secret` (must never reach the model), marker file conventions mirrored from `TestMcpServer`.
- **The core addition**: `ExtensionMcpServers` exposes `Statuses`; `/mcp` merges the configured and extension-registered servers' statuses (same segment shape: `name — started, N tools` / `not started` / `failed: reason`); "no servers configured" only when both sets are empty.
- **Tests**: kit-level (registration collected with its name, command, exposure); production composition (the exact `ExtensionMcpServers` mapping the session uses, via `InternalsVisibleTo` for the sample's test assembly) → namespaced `fake__tool` names and exposures on the tool list; a scripted run where `fake__echo` is declared to the model and round-trips through approval (risk `Execute`) while `fake__secret` never appears; env/marker proof; and an interactive `/mcp` test in `Lunate.Coding.Tests` (extension store + the real session) showing the extension server listed with its status.

## Impact

- `samples/extensions/mcp-from-extension/`: extension, fake server, tests, README; projects join `lunate.sln`; layering allowlist gains the sample edges; `Lunate.Coding.csproj` gains `InternalsVisibleTo("McpFromExtensionExtension.Tests")` (precedent: `Lunate.Protocols.Tests`, `Lunate.Extensibility.Testing.Tests`).
- `src/Lunate.Coding/`: `ExtensionMcpServers.Statuses`, `/mcp` merge in `InteractiveSession.Commands.cs`.
- Specs: tui (Slash commands — the `/mcp` clause; a new scenario for extension-registered servers).

## Out of scope

- Failure-notice parity for extension servers (the extensions spec's contract is "logged by the host"; config servers notify — harmonizing that is a separate question; noted, not changed here).
- Name collisions between a configured and an extension-registered server of the same name (both currently contribute tools; not T-46's question).
- Model providers, subagents, code mode and the remaining fitness samples (their cards).
