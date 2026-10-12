# Design: sample-mcp-from-extension

## Context

T-46: a reference extension contributing an MCP server. Facts the design leans on (verified against the tree):

- `McpServerRegistration(Name, Config, Exposure = Direct, ExposureOverrides?)` and `McpServerConfig(Command, Arguments, Environment?, WorkingDirectory?, CallTimeout?)` — the `mcp.json` shape (`Abstractions/Mcp/McpServerRegistration.cs`). `ToolExposure`: `Direct | ModelOnly | Programmatic | Deferred | Hidden`.
- `IExtensionContext.RegisterMcpServer` — "registration is registration only: it must not start processes, sockets or timers. The first registration per server name wins; a duplicate is refused and logged. The process starts on the first tool listing." The loader collects registrations at creation (`ExtensionLoader.McpServers`, public).
- Namespacing and exposure live in `Lunate.Protocols/McpToolAdapter.cs` (public): `Name => "{server}__{tool}"`, `Exposure`, `Namespace`. The Coding mapping `ExtensionMcpServers` (internal) wraps `McpToolSet` (internal) — one host per registration, lazily started on first `ListToolsAsync`, failures logged naming server + extension, session continues. `InteractiveSession` merges those tools into the registry (`:284`) and separately composes the `mcp.json` set (`_mcp`).
- **The gap**: `/mcp` (`InteractiveSession.Commands.cs` `McpStatusText`) reads only `_mcp.Statuses` (the configured set). Extension-registered servers' statuses are unreachable — and `_mcp is null` returns "mcp: no servers configured" even when an extension registered one.
- The interactive test host supports real extension stores (`ExtensionSeams.StorePath`; `ExtensionCompositionEndToEndTests` precedent), so the `/mcp` merge is testable end-to-end.
- `tests/tools/TestMcpServer` (official `ModelContextProtocol` 2.2.0, marker via `LUNATE_TEST_MCP_MARKER`, tools pre-added before `McpServer.Create`) is the proven pattern the sample's fake mirrors. The package is already in the repo (T-29) — not a new dependency.

## Decisions

### D1 — The sample registers one server, with the marker in the config's environment

Factory calls `context.RegisterMcpServer(new McpServerRegistration("fake", config, Exposure: Direct, ExposureOverrides: {"secret": Hidden}))` where the config is `dotnet <FakeMcpServer.dll>` with `Environment = {"LUNATE_FAKE_MCP_MARKER": <path from settings>}`. The marker path comes from the extension's settings (`markerPath`, optional) so the test proves both the registration config's env passing and the process lifecycle. No manifest field — registrations happen at creation (the T-41 contract).

### D2 — The fake server mirrors TestMcpServer, minimally

`FakeMcpServer` console project (in the sample, referenced by the tests so its dll lands in the test output): official SDK, stdio only, tools added before `McpServer.Create`:
- `echo` (text → text), `add` (int,int → structured `{sum}`), `secret` (text → text; the tool that must never be declared to the model).
- Marker file: `started pid=<n>` at startup (via the env var), appended lines for calls; everything else to stderr. Mirroring the TestMcpServer conventions keeps one mental model for both.

### D3 — The composition proof uses the production mapping, via `InternalsVisibleTo`

The sample's tests build `ExtensionMcpServers` over `host.Runner.McpServers.List()` exactly as `ExtensionSession` does, list once, and add the tools to the kit's registry. That is the real seam (not a re-implementation): `Lunate.Coding.csproj` gains `InternalsVisibleTo("McpFromExtensionExtension.Tests")` — precedent: `Lunate.Protocols.Tests` and `Lunate.Extensibility.Testing.Tests` are already IVT'd into Coding. The layering allowlist gains the sample's edges (precedent: the lsp sample's six edges).

### D4 — `/mcp` merges both sets

`ExtensionMcpServers` gains an internal `Statuses` passthrough. `McpStatusText()` becomes: statuses = configured (`_mcp?.Statuses ?? []`) + extension (`_extensions?.McpServers.Statuses ?? []`), same segment shape and join; "no servers configured" only when both are empty (and still when the extension host is absent). No new labels — names are the identity; a cross-set name collision would show two segments (noted in the proposal as out of scope).

### D5 — Tests

Sample (`McpFromExtensionExtension.Tests`, kit + production mapping):
- Registration collected: `host.Runner.McpServers.List()` has `fake` with the right command/exposure and the override.
- Composition: tools listed once (marker proves one start), names `fake__echo` / `fake__add` / `fake__secret`, exposures `Direct`/`Direct`/`Hidden` on the tool list; a second `ListToolsAsync` does not restart (marker count stays one).
- Scripted run: `fake__echo` is declared to the model (the scripted client's first request contains the name), a call round-trips through the approval prompt (risk `Execute` → the scripted approver) with the echoed output; `fake__secret` appears in neither the declaration nor a callable path; `fake__add`'s structured result rides through (T-30 mapping behavior, asserted on the tool result).
- Failure isolation: a fake that cannot start (bad command via a second registration in a test-local variant, or a config pointing at a missing binary) → logged with server + extension named, session continues (no tools from it). Keep this at the kit level with a test-local registration, not a second sample server.
- `Lunate.Coding.Tests` (interactive path): a temp extension store with a minimal test extension registering an MCP server (the fake server dll) → `/mcp` notice lists it with `started, N tools`; and with no configured servers the notice is NOT "no servers configured". Precedent for the store pattern: `ExtensionCompositionEndToEndTests`.

### D6 — Spec delta

tui "Slash commands": the `/mcp` clause reads "lists the configured and extension-registered MCP servers with their status"; the original scenario stays verbatim and a new scenario covers the extension-registered case. No other capability changes.

## Alternatives considered (devil's advocate)

- **Re-implement the registration → options mapping in the tests** (avoid IVT): rejected — that tests a copy, not the product seam; the fitness point is the real composition.
- **Kit-level MCP support** (so samples don't need IVT): rejected for now — the kit cannot reference Coding (layering), and the sample tests already reference Coding for the real tools (precedent).
- **Assert `/mcp` only at the formatter level**: rejected — the interactive path with a real extension store exists and is the honest surface.
- **A second sample server for the failure case**: rejected — the failure path is already covered by `ExtensionMcpServersTests`; the sample pins the happy path plus one test-local failing registration.
- **Harmonize failure notices** (extension failures → TUI notice like config failures): deferred — the extensions spec says "logged by the host"; changing that is its own question.

## Risks

- **IVT widening**: one test assembly sees Coding internals — precedent exists; the alternative (public surface for the mapping) is a bigger change than the card warrants.
- **SDK version coupling**: the fake pins `ModelContextProtocol` 2.2.0 like `tests/tools/TestMcpServer`; a future bump moves both together.
- **Process-spawn timing on CI**: the sample spawns a `dotnet` process per test — the lsp sample's lesson applies: serialize the suite (`Parallelization(Mode = None)`) from the start and keep budgets under the runner's per-handler timeout.
- **Windows paths**: marker paths and tool names are platform-neutral; expected strings built with `Path.Combine` where the code builds paths.

## Deviations

*(the apply records deviations here)*
