# 0020 — ACP SDK choice: LibAcp, behind `IAcpServer`

- Status: accepted — 2026-10-10 (maintainer sign-off: "Use libacp"; recommendation adopted)
- Date: 2026-10-10
- Relates to: guide §Frontends (ACP mode), §ACP server; OpenSpec capability `protocols` (T-27; permissions and editor FS follow in T-28)
- Survey date: 2026-10-10 (versions, activity and download counts as of that day)

## Context

Lunate ships an ACP server mode (`lunate --acp`) so editors such as Zed or JetBrains drive it over stdio. There is no official C# SDK for the Agent Client Protocol; the guide names three community candidates to compare on **spec coverage, activity and license**, with the choice recorded here. The integration sits behind `IAcpServer` so a later switch stays cheap.

Requirements from our side (agent role):

- `initialize`, `session/new`, `session/prompt` with streaming `session/update`, `session/cancel` (T-27); `session/request_permission`, `fs/read_text_file`/`fs/write_text_file` follow in T-28.
- An in-process client over a pipe pair for tests (guide §Tests).
- `System.Text.Json` (no Newtonsoft), a small dependency closure, .NET 10.
- Permissive license and inspectable source — we may need to fix or fork.

## Candidates (evidence)

**AcpSdk** — v1.2.0, MIT, published 2026-02-04; 613 downloads; owner `ArbDevBest` (unverified). Built on Microsoft's `StreamJsonRpc` + `Nerdbank.Streams` — a strong transport foundation — but the repository linked from the package README (`arbDEVbest/AgentClientProtocol_CsharpSDK`) returns 404, no release has shipped in eight months, and its agent-side cancel/permission behaviour cannot be inspected — exactly the paths we must rely on.

**AgentClientProtocol** (nuskey8/acp-csharp) — v0.1.5, MIT, 20 stars, last push 2025-11-05; the most-used C# ACP package (~2.8k downloads). A maintained fork (Wixely, 2026-07-28) documents agent-side defects that remain unfixed upstream: `session/cancel` starvation, a permission-request deadlock, EOF handling and omitted `stopReason` serialization. Cancel and permissions are precisely our required paths, and the project is stale.

**LibAcp** (devsanity-ai/libACP, formerly sargemonkey) — v0.1.0, MIT, published 2026-05-18, repo active to 2026-05-26. Hand-written to the stable v1 spec, modelled on the official TypeScript SDK: full agent-side surface (`initialize`, `session/new|load|resume|list|close`, `session/prompt|cancel|update`, `session/set_mode|set_config_option`, `session/request_permission`, `fs/read_text_file|write_text_file`, `terminal/*`), `System.Text.Json` end-to-end with discriminated-union converters, newline-delimited JSON over any `Stream` pair (our in-process pipe tests), a single dependency (`Microsoft.Extensions.Logging.Abstractions`), 43 tests and CI on Linux/Windows/macOS, multi-targeting `net8.0`/`net10.0`. Early version and small adoption.

Also surveyed and rejected: `dotacp` (Newtonsoft dependency, beta), `Agentic.ACPLibrary` (no adoption, unproven), `yetsmarch.AgentClientProtocol` / `AgentClientProtocol4CSharp` (duplicate package ids, small), `Acp.Net.*` (testing helpers only).

## Decision

Adopt **LibAcp** (`LibAcp` on NuGet, MIT) in `Lunate.Protocols`, behind the `IAcpServer` interface. It is the only candidate that is source-inspectable, spec-complete on the agent side including cancel and permissions, `System.Text.Json`-native with a minimal closure, and designed for stream-pair transports — the shape our test strategy and stdio mode need. The alternatives fail on trust (AcpSdk: no reachable source) or on exactly our critical paths (AgentClientProtocol: stale, agent-side cancel/permission defects).

## Consequences

- `Lunate.Protocols` gains the LibAcp package (per the "no new packages without asking" rule, this ADR is the ask).
- Our own end-to-end pipe tests guard the SDK's behaviour in CI; a stalled or buggy upstream is forkable (MIT).
- The `IAcpServer` seam keeps a switch cheap: re-evaluate (including maintained forks of the alternatives) if LibAcp stalls or a defect blocks us.
- Pin the exact LibAcp version; upgrades are deliberate, checked against the stable v1 schema.
