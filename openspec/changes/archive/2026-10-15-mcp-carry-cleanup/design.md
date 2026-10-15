# Design — mcp-carry-cleanup

## D1 — The override-log invariant: enforcement at the internal seam

The spec scenario "An unknown override is logged, never silently ignored" depends on `McpServerHost` receiving a log delegate. The delegate is optional today (`Action<string>? log = null`) and used only for that warning, so the invariant is call-site-dependent.

- **Why not make the public parameter required:** the constructor lives in `PublicAPI.Unshipped.txt` (pre-ship), so tightening it would be legal; the change deliberately does not — AGENTS.md's policy is that public types change only when the change says so, and this carry's scope is enforcement + coverage, not an API break. The optionality remains for tests, which legitimately omit the delegate.
- **Enforcement point:** `McpToolSet` (internal to `Lunate.Coding`) is the **only production construction path** for `McpServerHost` (verified by grep: `src/` has exactly one `new McpServerHost(` — `McpToolSet.cs`). Making its `log` parameter required non-nullable (plus `ArgumentNullException.ThrowIfNull`) makes `serverLog ?? log` non-null by construction: no future production caller can silently drop the warning.
- **The public seam is documented:** the parameter comment states the invariant, that production always supplies a log, and that the optionality remains for tests.
- **Asserted where it belongs:** `McpExposureTests.An_override_naming_no_listed_tool_is_logged_and_the_rest_proceeds` already passes a recording log and asserts the warning (kept as-is); `Exposure_and_overrides_follow_the_options_and_nothing_is_logged` pins the no-warning case.

## D2 — `TestMcpServer` resolution: one shared home

The `TestMcpServer` tool is located in three copies today: `Lunate.Protocols.Tests/McpTestEnvironment.cs`, `Lunate.Coding.Tests/McpTestServerSupport.cs`, and the private members of `Lunate.Extensibility.Testing.Tests/McpFromExtensionEndToEndTests.cs`. Each copy re-derives repository root, configuration, DLL path, dotnet host path and marker helpers.

- **Why a shared project rather than folding into `McpTestEnvironment`:** `McpTestEnvironment` is project-local to `Lunate.Protocols.Tests`; test projects cannot reference each other. A shared home must be its own project.
- **Shape:** `tests/Lunate.TestSupport/Lunate.TestSupport.csproj` — plain library (no test SDK, so the solution-level test run skips it), **BCL-only** (no `Lunate.*` references, so the layering graph gains no `From: Lunate.TestSupport` edges). One static class `TestMcpServer`: `RepositoryRoot`, `Configuration`, `ServerDll`, `DotnetPath`, `MarkerLines`, `WaitForMarkerLineAsync`, `StartedPid`, `IsProcessAlive`, `WaitForProcessExitAsync`, `KillAsync`.
- **Consumers:** the three projects delete their copies and use `TestMcpServer.*`; project-specific extras stay local (`McpTestEnvironment` keeps `Context`/`NullAgentEvents`/`Options`; `McpTestServerSupport` keeps `EntryJson`/`ConfigJson`).
- **Layering:** exactly three new edges declared in `LayeringChecker.AllowedReferences` — `(Lunate.Protocols.Tests, Lunate.TestSupport)`, `(Lunate.Coding.Tests, Lunate.TestSupport)`, `(Lunate.Extensibility.Testing.Tests, Lunate.TestSupport)`. The set-equality test (`Project_reference_graph_matches_allowed_edges`) enforces both directions.
- **Rejected alternative:** MSBuild `<Compile Include>` links — no precedent in the repo, worse discoverability than a project.

## D3 — Scenario coverage: one promotion, one documented split

- **Registration-only ("nothing is started and nothing is logged at registration"):** already asserted e2e for "nothing started" (the marker file must not exist after `StartAsync`). The promotion: also assert the kit's log is fully empty at that point through the `RecordingExtensionLog` — verified empirically; fully empty holds, so the e2e asserts it directly (stronger than a scoped check).
- **Duplicate refusal:** stays at registry + loader level — `McpServerRegistryTests` covers the registry refusal, and its loader-level test installs **two** extensions, discovers and loads both through the real loader, asserting the refusal names both. Promoting to the kit's e2e path would require multi-extension host support for a single assertion — disproportionate. **Deliberate split, documented here.**

## D4 — Non-goals

No behaviour change anywhere; no spec deltas (`skip_specs` — the invariants are already spec'd); no public-API change; no new packages; no kit changes.
