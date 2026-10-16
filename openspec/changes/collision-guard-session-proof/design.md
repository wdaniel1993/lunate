# Design — collision-guard-session-proof

## D1 — The reachable collision, and why the test uses it

Tool-level names are `{server}__{tool}` for MCP tools and `{extensionId}__{toolName}` for
extension tools. Walking the composition pipeline (`InteractiveSession.ComposeMcpToolsAsync`):

- **Same-name server (extension registration vs configured `mcp.json`)**: unreachable as a
  tool-level collision — `McpNameCollisions` keeps the registration out of the listing (configured
  wins, registration shown shadowed) before any tool is listed.
- **Two configured servers with the same name**: unreachable — a JSON map cannot repeat a key.
- **Two extension registrations with the same name**: unreachable — the extension MCP-server registry keeps the first registration.
- **A direct extension tool vs an MCP tool**: **reachable and the intended case.** Direct extension
  tools are added first, unguarded (`extensions.Tools`, design D7 order); the later MCP tool with
  the same model-facing name hits the guard and is skipped with the diagnostic.

The test therefore collides a direct extension tool (`ToolFixture.Install(store, id: "test")` →
`test__echo`) with a configured server (`McpTestServerSupport.ConfigJson("test")` → server `test`,
tool `echo` → `test__echo`), and proves all three observable consequences through the real
session: exactly one declaration (the extension's description in the model-facing declarations, the
MCP description absent), the skip diagnostic (naming `mcp.json` and the extension namespace), and
execution identity (the scrollback shows the extension handler's `echo: args=…` output, never the
MCP server's).

The diagnostic arrives from the *configured-server listing*, so the frame wait for it is
spawn-dependent and uses the 30s cold-runner window (`SpawnWait`, the MCP interactive E2E idiom).

## D2 — Dedupe scope decision: fine-by-design, source-agnostic

The guard skips **any** later duplicate, whatever its source. Decision: keep it, and document it.

- **Uniform beats narrow**: the guard's job is "a name collision never crashes the session; first
  composition wins with a diagnostic". Narrowing it to extension-vs-MCP would let any other
  duplicate reach the registry's raw throw — a session-killing outcome for a case the guard could
  have degraded gracefully.
- **The dangerous cases are unreachable anyway** (D1): the source-agnostic branch is
  belt-and-braces, not a behaviour path users can hit today; if a future change ever made an
  MCP-vs-MCP duplicate reachable, skip+diagnostic is strictly the safer degradation.
- **Visibility is preserved**: every skip emits one single-line diagnostic naming both sources, so
  even an unexpected skip is observable, not silent.

The decision is recorded in `ToolComposition`'s doc comment (where the next reader looks) and in
this design. No code narrowing.
