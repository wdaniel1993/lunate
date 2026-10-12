# Proposal: sample-mcp-from-extension

## Why

Guide card T-46 (phase 6): the fitness suite proves `RegisterMcpServer` — a reference extension contributing an MCP server, with its tools namespaced and exposure-gated, visible in the TUI. Building the sample exposed real gaps in the MCP surface: `/mcp` lists only the **configured** servers (an extension-registered server's status never reaches it), a name collision between a configured and an extension-registered server would currently throw in the tool registry (duplicate names fail fast, with no rule saying who wins), and a server that crashes after listing leaves a stale "started" status. The sample's done-when ("namespaced MCP tools visible in the TUI") cannot hold without closing these.

## What

- **The sample** (`samples/extensions/mcp-from-extension/`): an extension that registers one MCP server in its factory — the `mcp.json` shape, a start-marker via the config's environment, exposure `Direct` and a per-tool override (`secret` → `Hidden`). Registration starts nothing; the first listing starts the server exactly once (the existing lazy contract).
- **A fake MCP server** (`FakeMcpServer`, console project in the sample): built on the official `ModelContextProtocol` SDK already used by `tests/tools/TestMcpServer` (no new package) — `echo` (text), `add` (structured), `secret` (must never reach the model or nested execution), `crash` (exits the process, for the liveness story); marker file conventions mirrored from `TestMcpServer`.
- **The kit gains a public composition**: `ExtensionTestHost` composes extension MCP registrations through the **production mapping** (`ExtensionMcpServers`; the kit uses Coding internals) and exposes the composed tools + statuses — so samples prove the public surface a third-party extension gets, without IVT of their own. The kit's own MCP end-to-end test moves onto that public API.
- **The MCP surface closes its gaps**: `/mcp` merges configured and extension-registered servers and shows each one's **source** (config file path / `extension '{id}'`), never environment values or headers; a **name collision** is shown with the configured server winning (the extension registration is skipped before listing, in every mode); a server whose process **crashes** flips to failed ("the server process exited") when next observed and fires one notice; its tools read as unavailable in `/mcp`.
- **Tests**: registration visibility; namespaced names + exposures on the tool list (one start per session); `fake__echo` declared to the model and round-tripping through approval while `fake__secret` is neither model-visible nor callable via nested execution; structured results; failure isolation; unload/stop kills the process; crash → failed in `/mcp` + notice; `/mcp` output contains no env values; and the interactive `/mcp` tests in `Lunate.Coding.Tests` (extension store; collision; no secrets). Suite serialized from the start (the lsp CI lesson) — with a follow-up card for per-test isolation so parallelism can return.

## Impact

- `samples/extensions/mcp-from-extension/`: extension, fake server, tests, README; projects join `lunate.sln`; layering allowlist gains the sample edges (sample test → kit + extension only).
- `src/Lunate.Extensibility.Testing/`: reference to `Lunate.Coding` + allowlist edge; public `ComposeMcpServersAsync` + status DTO on the host; `Lunate.Coding.csproj` gains `InternalsVisibleTo("Lunate.Extensibility.Testing")`.
- `src/Lunate.Coding/` + `src/Lunate.Protocols/`: per-server source (set at config load / by the extension mapping); `ExtensionMcpServers.Statuses`; collision filter (config wins) shared by all three modes; crash detection → status + notice; `/mcp` merge.
- Specs: extensions (MCP servers from extensions — collision rule, crash status); tui (Slash commands — `/mcp` clause, sources, shadowing, no secrets).

## Out of scope

- Restoring parallel test execution (card filed: per-test isolation for process-spawning suites).
- HTTP-transport MCP servers with headers (the "never print env values or headers" rule holds regardless; no such server type exists yet).
- Model providers, subagents, code mode and the remaining fitness samples (their cards).
