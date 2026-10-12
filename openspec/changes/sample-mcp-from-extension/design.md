# Design: sample-mcp-from-extension

## Context

T-46: a reference extension contributing an MCP server. Facts the design leans on (verified against the tree):

- `McpServerRegistration(Name, Config, Exposure = Direct, ExposureOverrides?)` and `McpServerConfig(Command, Arguments, Environment?, WorkingDirectory?, CallTimeout?)` — the `mcp.json` shape (`Abstractions/Mcp/McpServerRegistration.cs`). `ToolExposure`: `Direct | ModelOnly | Programmatic | Deferred | Hidden`.
- `IExtensionContext.RegisterMcpServer` — registration only; first registration per server name wins (extension-vs-extension); the process starts on the first tool listing. `ExtensionLoader.McpServers` is public.
- Namespacing and exposure live in `Lunate.Protocols/McpToolAdapter.cs` (public): `Name => "{server}__{tool}"`, `Exposure`, `Namespace`. The Coding mapping `ExtensionMcpServers` (internal) wraps `McpToolSet` (internal); `ExtensionSession` builds it from the loader's registrations; all three modes (interactive `:284`, print, ACP) merge its tools into the registry and separately compose the `mcp.json` set.
- **Gap 1**: `/mcp` (`InteractiveSession.Commands.cs` `McpStatusText`) reads only `_mcp.Statuses`; extension statuses are unreachable ("no servers configured" even when one is registered).
- **Gap 2**: no rule covers a configured-vs-extension name collision; `ToolRegistry.Add` throws on duplicate names ("a collision is a wiring bug") — so a collision currently breaks composition in every mode.
- **Gap 3**: a server that exits after listing keeps its "started" status; `McpServerHost` holds the SDK client (no process handle exposed), so liveness is a probe (a client round trip), not a handle check.
- `McpConfig.Load` (Protocols) knows each server's file; `McpConfigResult` drops the origin. `McpServerOptions` (Protocols) is the per-server carrier.
- The kit (`Lunate.Extensibility.Testing`) references Agent/Ai/Extensibility(+Abstractions); its own test suite already exercises `ExtensionMcpServers` via IVT (`Lunate.Extensibility.Testing.Tests → Lunate.Coding` allowlist edge + IVT). `ToolCallingChatClient` (kit) drives tool calls.
- `tests/tools/TestMcpServer` (official `ModelContextProtocol` 2.2.0, marker via `LUNATE_TEST_MCP_MARKER`) is the proven pattern the sample's fake mirrors; the package is already in the repo.

## Decisions

### D1 — The sample registers one server, with the marker in the config's environment

Factory calls `context.RegisterMcpServer(new McpServerRegistration("fake", config, Exposure: Direct, ExposureOverrides: {"secret": Hidden}))`; config = `dotnet <FakeMcpServer.dll>` with `Environment = {"LUNATE_FAKE_MCP_MARKER": <settings markerPath>}` (settings schema: `markerPath`). No manifest field — registrations happen at creation.

### D2 — The fake server mirrors TestMcpServer, minimally

Console project in the sample (referenced by the tests so its dll lands in the test output): official SDK, stdio only, tools added before `McpServer.Create` — `echo` (text), `add` (int,int → structured `{sum}`), `secret` (text; the tool that must never reach the model), `crash` (exits the process; the liveness story). Marker file: `started pid=<n>` at startup via the env var, call lines appended; everything else to stderr.

### D3 — The kit gets the public composition (maintainer steer: no sample IVT)

`ExtensionTestHost` gains `ComposeMcpServersAsync(ct)`: builds `ExtensionMcpServers` over `Runner.McpServers.List()` exactly as `ExtensionSession` does, lists once, adds the tools to `Tools`, and returns public status records (name, state, tool count, reason, source) + a public state enum; the composed set is owned by the host and disposed on `StopAsync`/`DisposeAsync` (servers stop). Plumbing: `Lunate.Extensibility.Testing.csproj` references `Lunate.Coding` (allowlist edge `Testing → Coding`); `Lunate.Coding.csproj` gains `InternalsVisibleTo("Lunate.Extensibility.Testing")`. The kit's own `McpFromExtensionEndToEndTests` moves onto the public API (it keeps proving the internals-level behavior through it). The sample's tests use only the kit + the extension — the public surface a third-party extension gets.

### D4 — `/mcp` merges both sets, with sources, never secrets

- Per-server source: `McpServerOptions` gains `Source`; `McpConfig.Load` sets it to the file path it read; `ExtensionMcpServers` sets `extension '{id}'`. `McpServerStatus` gains `Source`.
- `McpStatusText()` merges configured + extension statuses; segment shape: `name — started, N tools (source)` / `not started` / `failed: reason`; "no servers configured" only when both sets are empty.
- The notice never prints environment values or headers (the status records never carry them; a test asserts with a sentinel).

### D5 — Collisions: the configured server wins, in every mode

A shared filter (small helper in Coding) takes the config server names + the registrations and returns (kept, shadowed); each mode skips shadowed registrations before listing (no duplicate names reach the registry) and logs the collision naming both sources. Interactive additionally shows the shadowed entry in `/mcp` (`name — shadowed by {configured source}`). The filter runs where both sets are known — the interactive compose order (extensions first, config later) needs the config names before the extension listing; the implementation may reorder the compose phases or defer the extension listing — behavior is what the spec pins.

### D6 — Crash: status flips, one notice, tools read unavailable

Detection is probe-based (`McpServerHost` exposes no process handle): a started server is liveness-checked at the next observation — `/mcp` and after a failed tool call — via a bounded client round trip; a dead server flips to `Failed` with reason `the server process exited` and fires **one** notice per server (deduped on the Started→Failed transition; where a notice surface exists — interactive). Tool calls to a dead server keep their existing actionable error result. The fake's `crash` tool makes this deterministic.

### D7 — Tests

Sample (`McpFromExtensionExtension.Tests`, kit-only public surface):
- Registration collected: name/command/exposure/override; nothing started (no marker).
- Composition via `ComposeMcpServersAsync`: tools `fake__echo` / `fake__add` / `fake__secret` / `fake__crash` with exposures `Direct`/`Direct`/`Hidden`/`Direct`; one start (marker); a second listing does not restart.
- Scripted run: `fake__echo` declared to the model, call round-trips through approval (risk `Execute`) with the echoed output; `fake__add` structured result asserted; `fake__secret` is **neither model-visible nor callable via nested execution** (a nested-call probe à la the lsp sample attempts it → refused; and absent from declarations).
- Failure isolation: a test-local registration with a missing binary → logged naming server + extension, session continues.
- Unload/stop stops the server: after `StopAsync`/`DisposeAsync` the fake's PID is gone (the lsp sample's PID-dead assertion pattern).
- Crash: call `fake__crash` → the call errors; the server then reads failed ("the server process exited") with one notice; its tools read unavailable.
- Suite serialized from the start (`Parallelization(Mode = None)`); budgets under the runner's per-handler timeout.

`Lunate.Coding.Tests` (interactive path): temp extension store + a test extension registering the fake server → `/mcp` lists it with status + `extension '{id}'` source; a configured server + a colliding registration → collision shown, configured wins; sentinel env values → absent from the notice. Precedent: `ExtensionCompositionEndToEndTests` store pattern.

### D8 — Follow-up card (maintainer ask)

Per-test isolation for process-spawning suites (per-test temp dirs + unique markers, no shared state) so serialization can be lifted and suites run in parallel again. Filed at drafting time.

## Alternatives considered (devil's advocate)

- **IVT for the sample's test assembly** (the pre-sign-off plan): rejected by the maintainer — samples prove the public surface; the kit carries the internals instead.
- **Kit-level MCP support with its own composition copy**: rejected — the kit must use the production mapping (`ExtensionMcpServers`), not a re-implementation.
- **Process-handle liveness check**: rejected — the SDK client hides the process; a bounded probe is the honest mechanism. (If a future SDK exposes the handle, swap the probe.)
- **Reorder-to-fix collisions by sorting sets**: rejected — order alone can't decide; the filter makes config-wins explicit and testable in all modes.
- **Notice parity for extension failures beyond crashes**: still out of scope; the spec's crash sentence covers the required case.

## Risks

- **Kit → Coding dependency**: the kit becomes heavier (pulls the app assembly). Accepted per the steer; the alternative (public Coding surface for the mapping) is a larger API change.
- **Probe cost**: the `/mcp` liveness probe is one bounded round trip per started server; bounded and only on the command path.
- **Compose-order change**: interactive composes extensions before the config set; the collision filter needs both — the reorder/defer is small but touches startup; tests pin the behavior (no duplicate throw, configured wins).
- **Process-spawn timing on CI**: serialized suite + budgets under the runner timeout (the lsp lesson).
- **Windows paths**: marker paths and expected strings built with `Path.Combine` where the code builds paths.

## Deviations

- **Compose order and the deferred mapping**: the extension host still loads first (interactive trust prompts keep the pinned extension-before-`mcp.json` order asserted by `TrustPromptEndToEndTests`), and the extension MCP *listing* is deferred: `ExtensionSession.ComposeMcpServers(configured)` builds the `ExtensionMcpServers` mapping after the config set is composed, applying `McpNameCollisions.Filter` there. `ExtensionSession.StartAsync` no longer takes a filter; `McpServers` is null until the mapping is built. Print and ACP follow the same shape (load, config compose, mapping, extension listing, config listing), keeping the extension-tools-first registration order.
- **Crash log line**: `McpToolSet` logs the exit once per server through its extension log in every mode (the kit's sample tests assert "exactly one" against that surface); the interactive user notice rides the same transition. D6 said "where a notice surface exists — interactive"; the log line is the non-interactive trace.
- **Exited servers stay failed**: `McpToolSet.ListToolsAsync` skips a server already observed exited, so a later listing cannot resurrect it to Started; its stale tools are not handed out again.
- **Kit re-composition**: a second `ExtensionTestHost.ComposeMcpServersAsync` call re-observes liveness (`ObserveStatusesAsync`) instead of re-listing; statuses refresh, servers do not restart and tools are not added twice. The crash sample test depends on this observation point.
- **`/mcp` notice vs crash notice**: in the `/mcp` path the crash notice and the status notice are posted in the same command turn, so the live area shows the status notice (`name — failed: the server process exited`), which names the server and the reason; the crash notice stays observable on the failed-tool-call path. The transition (and its dedupe) fires exactly once either way.
- **`McpStatusText` → `McpStatusTextAsync`**: building the notice now liveness-probes started servers, so the private `/mcp` formatter became async; the segment shapes are unchanged.
- **Sample fake lookup**: the factory resolves `FakeMcpServer.dll` next to its own assembly (`Assembly.Location`), matching the sample's bundled-server story; the test build copies the fake's output beside the extension.
